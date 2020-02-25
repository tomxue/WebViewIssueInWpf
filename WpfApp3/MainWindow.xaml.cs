using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfApp3
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public class myBridge
        {
            public void SetTitle(string title)
            {
                Debug.WriteLine("SetTitle is executing...title = {0}", title);
            }

            public void PlayTTS(string tts)
            {
                Debug.WriteLine("PlayTTS is executing...tts = {0}", tts);
            }
        }

        public MainWindow()
        {
            this.InitializeComponent();

            this.wv.IsScriptNotifyAllowed = true;
            this.wv.ScriptNotify += Wv_ScriptNotify;
            //wv.RegisterName("wtjs", new myBridge());

            this.Loaded += MainPage_Loaded;
        }

        private void Wv_ScriptNotify(object sender, Microsoft.Toolkit.Win32.UI.Controls.Interop.WinRT.WebViewControlScriptNotifyEventArgs e)
        {
            textBlock.Text = e.Value;

            //返回结果给html页面
            //await this.wv.InvokeScriptAsync("recieve", new[] { "hehe, 我是个结果" });
        }

        private void setTitle(string str)
        {
            textBlock.Text = str;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            this.wv.Source = new Uri("http://cmsdev.lenovo.com.cn/musichtml/leHome/weather/index.html?date=&city=&mark=0&speakerId=&reply=");
            //this.wv.Source = new Uri("https://cmsdev.lenovo.com.cn/musichtml/leHome/weather/index.html?date=&city=&mark=0&speakerId=&reply=");

            //var html = File.ReadAllText("../../Assets\\index.html");
            //wv.NavigateToString(html);
        }
    }
}
