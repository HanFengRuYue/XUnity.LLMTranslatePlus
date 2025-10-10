using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            Title = "XUnity大语言模型翻译Plus";
            
            // 设置窗口大小
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
            
            // 默认导航到主页
            NavView.SelectedItem = NavView.MenuItems[0];
            ContentFrame.Navigate(typeof(HomePage));
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                string tag = selectedItem.Tag?.ToString() ?? "";
                
                Type pageType = tag switch
                {
                    "Home" => typeof(HomePage),
                    "ApiConfig" => typeof(ApiConfigPage),
                    "TranslationSettings" => typeof(TranslationSettingsPage),
                    "TextEditor" => typeof(TextEditorPage),
                    "Log" => typeof(LogPage),
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
