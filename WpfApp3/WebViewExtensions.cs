using Microsoft.Toolkit.Win32.UI.Controls.Interop.WinRT;
using Microsoft.Toolkit.Wpf.UI.Controls;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace WpfApp3
{
    // Source: https://github.com/dotnet/orleans/issues/1269#issuecomment-171233788
    public static class JsonHelper
    {
        private static readonly Type[] _specialNumericTypes = { typeof(ulong), typeof(uint), typeof(ushort), typeof(sbyte) };

        /// <summary>
        /// Converts values that were deserialized from JSON with weak typing (e.g. into <see cref="object"/>) back into
        /// their strong type, according to the specified target type.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <param name="targetType">The type the value should be converted into.</param>
        /// <returns>The converted value.</returns>
        public static object ConvertWeaklyTypedValue(object value, Type targetType)
        {
            if (targetType == null)
                throw new ArgumentNullException(nameof(targetType));

            if (value == null)
                return null;

            if (targetType.IsInstanceOfType(value))
                return value;

            var paramType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (paramType.IsEnum)
            {
                if (value is string)
                    return Enum.Parse(paramType, (string)value);
                else
                    return Enum.ToObject(paramType, value);
            }

            if (paramType == typeof(Guid))
            {
                return Guid.Parse((string)value);
            }

            if (_specialNumericTypes.Contains(paramType))
            {
                if (value is BigInteger)
                    return (ulong)(BigInteger)value;
                else
                    return Convert.ChangeType(value, paramType);
            }

            if (value is long || value is double)
            {
                return Convert.ChangeType(value, paramType);
            }

            return value;

            //throw new ArgumentException($"Cannot convert a value of type {value.GetType()} to {targetType}.");
        }
    }

    public enum WebViewInteropType
    {
        Notify = 0,
        InvokeMethod = 1,
        InvokeMethodWithReturn = 2,
        GetProperty = 3,
        SetProperty = 4
    }

    public class WebAllowedObject
    {
        public WebAllowedObject(WebView webview, string name)
        {
            WebView = webview;
            Name = name;
        }

        public WebView WebView { get; private set; }

        public string Name { get; private set; }

        public ConcurrentDictionary<(string, WebViewInteropType), object> FeaturesMap { get; } = new ConcurrentDictionary<(string, WebViewInteropType), object>();

        public EventHandler<WebViewControlNavigationCompletedEventArgs> NavigationCompletedHandler { get; set; }

        public EventHandler<WebViewControlScriptNotifyEventArgs> ScriptNotifyHandler { get; set; }
    }

    public static class WebViewExtensions
    {
        public static bool IsNotification(this WebViewControlScriptNotifyEventArgs e)
        {
            try
            {
                var message = JsonConvert.DeserializeObject<dynamic>(e.Value);

                if (message["___magic___"] != null)
                {
                    return false;
                }
            }
            catch (Exception) { }

            return true;
        }

        public static void AddWebAllowedObject(this WebView webview, string name, object targetObject)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (targetObject == null)
                throw new ArgumentNullException(nameof(targetObject));

            if (webview.Tag == null)
            {
                webview.Tag = new ConcurrentDictionary<string, WebAllowedObject>();
            }
            else if (!(webview.Tag is ConcurrentDictionary<string, WebAllowedObject>))
            {
                throw new InvalidOperationException("WebView.Tag property is already being used for other purpose.");
            }

            var webAllowedObjectsMap = webview.Tag as ConcurrentDictionary<string, WebAllowedObject>;

            var webAllowedObject = new WebAllowedObject(webview, name);

            if (webAllowedObjectsMap.TryAdd(name, webAllowedObject))
            {
                var objectType = targetObject.GetType();
                var methods = objectType.GetMethods();
                var properties = objectType.GetProperties();

                var jsStringBuilder = new StringBuilder();

                jsStringBuilder.Append("(function () {");
                jsStringBuilder.Append("window['");
                jsStringBuilder.Append(name);
                jsStringBuilder.Append("'] = {");

                jsStringBuilder.Append("__callback: {},");
                jsStringBuilder.Append("__newUuid: function () { return ([1e7]+-1e3+-4e3+-8e3+-1e11).replace(/[018]/g, function (c) { return (c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> c / 4).toString(16); }); },");

                foreach (var method in methods)
                {
                    if (!method.IsSpecialName)
                    {
                        if (method.ReturnType == typeof(void))
                        {
                            webAllowedObject.FeaturesMap.TryAdd((method.Name, WebViewInteropType.InvokeMethod), method);
                        }
                        else
                        {
                            webAllowedObject.FeaturesMap.TryAdd((method.Name, WebViewInteropType.InvokeMethodWithReturn), method);
                        }

                        var parameters = method.GetParameters();
                        var parametersInString = string.Join(",", parameters.Select(x => x.Position).Select(x => "$$" + x.ToString()));

                        jsStringBuilder.Append(method.Name);
                        jsStringBuilder.Append(": function (");
                        jsStringBuilder.Append(parametersInString);
                        jsStringBuilder.Append(") {");

                        if (method.ReturnType != typeof(void))
                        {
                            jsStringBuilder.Append("var callbackId = window['" + name + "'].__newUuid();");
                        }

                        jsStringBuilder.Append("window.external.notify(JSON.stringify({");
                        jsStringBuilder.Append("source: '");
                        jsStringBuilder.Append(name);
                        jsStringBuilder.Append("',");
                        jsStringBuilder.Append("target: '");
                        jsStringBuilder.Append(method.Name);
                        jsStringBuilder.Append("',");
                        jsStringBuilder.Append("parameters: [");
                        jsStringBuilder.Append(parametersInString);
                        jsStringBuilder.Append("]");

                        if (method.ReturnType != typeof(void))
                        {
                            jsStringBuilder.Append(",");
                            jsStringBuilder.Append("callbackId: callbackId");
                        }

                        jsStringBuilder.Append("}), ");
                        jsStringBuilder.Append((method.ReturnType == typeof(void)) ? (int)WebViewInteropType.InvokeMethod : (int)WebViewInteropType.InvokeMethodWithReturn);
                        jsStringBuilder.Append(");");


                        if (method.ReturnType != typeof(void))
                        {
                            jsStringBuilder.Append("var promise = new Promise(function (resolve, reject) {");
                            jsStringBuilder.Append("window['" + name + "'].__callback[callbackId] = { resolve, reject };");
                            jsStringBuilder.Append("});");

                            jsStringBuilder.Append("return promise;");
                        }

                        jsStringBuilder.Append("},");
                    }
                }

                jsStringBuilder.Append("};");

                foreach (var property in properties)
                {
                    jsStringBuilder.Append("Object.defineProperty(");
                    jsStringBuilder.Append("window['");
                    jsStringBuilder.Append(name);
                    jsStringBuilder.Append("'], '");
                    jsStringBuilder.Append(property.Name);
                    jsStringBuilder.Append("', {");

                    if (property.CanRead)
                    {
                        webAllowedObject.FeaturesMap.TryAdd((property.Name, WebViewInteropType.GetProperty), property);

                        jsStringBuilder.Append("get: function () {");
                        jsStringBuilder.Append("var callbackId = window['" + name + "'].__newUuid();");
                        jsStringBuilder.Append("window.external.notify(JSON.stringify({");
                        jsStringBuilder.Append("source: '");
                        jsStringBuilder.Append(name);
                        jsStringBuilder.Append("',");
                        jsStringBuilder.Append("target: '");
                        jsStringBuilder.Append(property.Name);
                        jsStringBuilder.Append("',");
                        jsStringBuilder.Append("callbackId: callbackId,");
                        jsStringBuilder.Append("parameters: []");
                        jsStringBuilder.Append("}), ");
                        jsStringBuilder.Append((int)WebViewInteropType.GetProperty);
                        jsStringBuilder.Append(");");

                        jsStringBuilder.Append("var promise = new Promise(function (resolve, reject) {");
                        jsStringBuilder.Append("window['" + name + "'].__callback[callbackId] = { resolve, reject };");
                        jsStringBuilder.Append("});");

                        jsStringBuilder.Append("return promise;");

                        jsStringBuilder.Append("},");
                    }

                    if (property.CanWrite)
                    {
                        webAllowedObject.FeaturesMap.TryAdd((property.Name, WebViewInteropType.SetProperty), property);

                        jsStringBuilder.Append("set: function ($$v) {");
                        jsStringBuilder.Append("window.external.notify(JSON.stringify({");
                        jsStringBuilder.Append("source: '");
                        jsStringBuilder.Append(name);
                        jsStringBuilder.Append("',");
                        jsStringBuilder.Append("target: '");
                        jsStringBuilder.Append(property.Name);
                        jsStringBuilder.Append("',");
                        jsStringBuilder.Append("parameters: [$$v]");
                        jsStringBuilder.Append("}), ");
                        jsStringBuilder.Append((int)WebViewInteropType.SetProperty);
                        jsStringBuilder.Append(");");
                        jsStringBuilder.Append("},");
                    }

                    jsStringBuilder.Append("});");
                }

                jsStringBuilder.Append("})();");

                var jsString = jsStringBuilder.ToString();

                webAllowedObject.NavigationCompletedHandler = (sender, e) =>
                {
                    var isExternalObjectCustomized = webview.InvokeScript("eval", new string[] { "window.external.hasOwnProperty('isCustomized').toString();" }).Equals("true");

                    if (!isExternalObjectCustomized)
                    {
                        webview.InvokeScript("eval", new string[] { @"
                            (function () {
                                var originalExternal = window.external;
                                var customExternal = {
                                    notify: function (message, type = 0) {
                                        if (type === 0) {
                                            originalExternal.notify(message);
                                        } else {
                                            originalExternal.notify(JSON.stringify({
                                                ___magic___: true,
                                                type: type,
                                                interop: message
                                            }));
                                        }
                                    },
                                    isCustomized: true
                                };
                                window.external = customExternal;
                            })();" });
                    }

                    webview.InvokeScript("eval", new string[] { jsString });
                };

                webAllowedObject.ScriptNotifyHandler = (sender, e) =>
                {
                    try
                    {
                        var message = JsonConvert.DeserializeObject<dynamic>(e.Value);

                        if (message["___magic___"] != null)
                        {
                            var interopType = (WebViewInteropType)message.type;
                            var interop = JsonConvert.DeserializeObject<dynamic>(message.interop.ToString());
                            var source = (string)interop.source.ToString();
                            var target = (string)interop.target.ToString();
                            var parameters = (object[])interop.parameters.ToObject<object[]>();

                            if (interopType == WebViewInteropType.InvokeMethod)
                            {
                                if (webAllowedObjectsMap.TryGetValue(source, out WebAllowedObject storedWebAllowedObject))
                                {
                                    if (storedWebAllowedObject.FeaturesMap.TryGetValue((target, interopType), out object methodObject))
                                    {
                                        var method = (MethodInfo)methodObject;

                                        var parameterTypes = method.GetParameters().Select(x => x.ParameterType).ToArray();

                                        var convertedParameters = new object[parameters.Length];

                                        for (var i = 0; i < parameters.Length; i++)
                                        {
                                            convertedParameters[i] = JsonHelper.ConvertWeaklyTypedValue(parameters[i], parameterTypes[i]);
                                        }

                                        method.Invoke(targetObject, convertedParameters);
                                    }
                                }
                            }
                            else if (interopType == WebViewInteropType.InvokeMethodWithReturn)
                            {
                                var callbackId = interop.callbackId.ToString();

                                if (webAllowedObjectsMap.TryGetValue(source, out WebAllowedObject storedWebAllowedObject))
                                {
                                    if (storedWebAllowedObject.FeaturesMap.TryGetValue((target, interopType), out object methodObject))
                                    {
                                        var method = (MethodInfo)methodObject;

                                        var parameterTypes = method.GetParameters().Select(x => x.ParameterType).ToArray();

                                        var convertedParameters = new object[parameters.Length];

                                        for (var i = 0; i < parameters.Length; i++)
                                        {
                                            convertedParameters[i] = JsonHelper.ConvertWeaklyTypedValue(parameters[i], parameterTypes[i]);
                                        }

                                        var invokeResult = method.Invoke(targetObject, convertedParameters);

                                        webview.InvokeScript("eval", new string[] { string.Format("window['{0}'].__callback['{1}'].resolve({2}); delete window['{0}'].__callback['{1}'];", source, callbackId, JsonConvert.SerializeObject(invokeResult)) });
                                    }
                                }
                            }
                            else if (interopType == WebViewInteropType.GetProperty)
                            {
                                var callbackId = interop.callbackId.ToString();

                                if (webAllowedObjectsMap.TryGetValue(source, out WebAllowedObject storedWebAllowedObject))
                                {
                                    if (storedWebAllowedObject.FeaturesMap.TryGetValue((target, interopType), out object propertyObject))
                                    {
                                        var property = (PropertyInfo)propertyObject;

                                        var getResult = property.GetValue(targetObject);

                                        webview.InvokeScript("eval", new string[] { string.Format("window['{0}'].__callback['{1}'].resolve({2}); delete window['{0}'].__callback['{1}'];", source, callbackId, JsonConvert.SerializeObject(getResult)) });
                                    }
                                }
                            }
                            else if (interopType == WebViewInteropType.SetProperty)
                            {
                                if (webAllowedObjectsMap.TryGetValue(source, out WebAllowedObject storedWebAllowedObject))
                                {
                                    if (storedWebAllowedObject.FeaturesMap.TryGetValue((target, interopType), out object propertyObject))
                                    {
                                        var property = (PropertyInfo)propertyObject;

                                        property.SetValue(targetObject, JsonHelper.ConvertWeaklyTypedValue(parameters[0], property.PropertyType));
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Do nothing
                    }
                };

                webview.NavigationCompleted += webAllowedObject.NavigationCompletedHandler;
                webview.ScriptNotify += webAllowedObject.ScriptNotifyHandler;
            }
            else
            {
                throw new InvalidOperationException("Object with the identical name is already exist.");
            }
        }

        public static void RemoveWebAllowedObject(this WebView webview, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            var allowedWebObjectsMap = webview.Tag as ConcurrentDictionary<string, WebAllowedObject>;

            if (allowedWebObjectsMap != null)
            {
                if (allowedWebObjectsMap.TryRemove(name, out WebAllowedObject webAllowedObject))
                {
                    webview.NavigationCompleted -= webAllowedObject.NavigationCompletedHandler;
                    webview.ScriptNotify -= webAllowedObject.ScriptNotifyHandler;

                    webview.InvokeScript("eval", new string[] { "delete window['" + name + "'];" });
                }
            }
        }
    }
}