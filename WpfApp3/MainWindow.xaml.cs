using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public MainWindow()
        {
            this.InitializeComponent();

            this.wv.ScriptNotify += Wv_ScriptNotify;

            this.Loaded += MainPage_Loaded;
        }

        private async void Wv_ScriptNotify(object sender, Microsoft.Toolkit.Win32.UI.Controls.Interop.WinRT.WebViewControlScriptNotifyEventArgs e)
        {
            //await (new MessageDialog(e.Value)).ShowAsync();
            textBlock.Text = e.Value;

            //返回结果给html页面
            await this.wv.InvokeScriptAsync("recieve", new[] { "hehe, 我是个结果" });
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            //我们事先写好了一个本地html页面用来做测试
            //this.wv.Source = new Uri("ms-appx-web://Assets/index.html");
            //this.wv.Source = new Uri("http://www.baidu.com");

            var html = File.ReadAllText("../../Assets\\index.html");
            wv.NavigateToString(html);
        }
    }
}
