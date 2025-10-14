using System;
using System.IO;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using XUnity_LLMTranslatePlus.Views;

namespace XUnity_LLMTranslatePlus
{
    /// <summary>
    /// 主窗口
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 设置窗口标题
            this.Title = "XUnity大语言模型翻译Plus";

            // 设置窗口图标（任务栏图标）- 使用绝对路径以支持发布后的单文件部署
            var iconPath = Path.Combine(AppContext.BaseDirectory, "ICON.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);

                // 同时设置标题栏图标
                try
                {
                    var bitmapImage = new BitmapImage(new Uri(iconPath));
                    TitleBarIcon.Source = bitmapImage;
                }
                catch
                {
                    // 如果加载失败，标题栏图标将保持为空
                }
            }

            // 配置自定义标题栏
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // 设置窗口大小
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));

            // 居中窗口
            CenterWindow();

            // 默认导航到主页
            NavView.SelectedItem = NavView.MenuItems[0];
            ContentFrame.Navigate(typeof(HomePage));
        }

        /// <summary>
        /// 将窗口居中显示在屏幕上
        /// </summary>
        private void CenterWindow()
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)?.WorkArea;
            if (area == null) return;

            var x = (area.Value.Width - AppWindow.Size.Width) / 2;
            var y = (area.Value.Height - AppWindow.Size.Height) / 2;
            AppWindow.Move(new PointInt32(x, y));
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                string tag = selectedItem.Tag?.ToString() ?? "";

                Type? pageType = tag switch
                {
                    "Home" => typeof(HomePage),
                    "ApiConfig" => typeof(ApiConfigPage),
                    "TranslationSettings" => typeof(TranslationSettingsPage),
                    "TextEditor" => typeof(TextEditorPage),
                    "Log" => typeof(LogPage),
                    "About" => typeof(AboutPage),
                    _ => null
                };

                if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
                {
                    ContentFrame.Navigate(pageType);
                }
            }
        }
    }
}
