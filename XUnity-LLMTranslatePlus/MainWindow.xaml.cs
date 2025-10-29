using System;
using System.IO;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using WinRT.Interop;
using XUnity_LLMTranslatePlus.Services;
using XUnity_LLMTranslatePlus.Views;

namespace XUnity_LLMTranslatePlus
{
    /// <summary>
    /// 主窗口
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly ConfigService? _configService;
        private bool _isLoadingTheme = false; // 防止加载主题时触发保存
        private string _currentTheme = "Default"; // 追踪当前主题

        public MainWindow()
        {
            InitializeComponent();

            // 获取配置服务
            _configService = App.GetService<ConfigService>();

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

            // 初始化任务栏进度服务
            InitializeTaskbarProgress();

            // 加载并应用主题设置
            LoadThemeSettings();
        }

        /// <summary>
        /// 初始化任务栏进度服务
        /// </summary>
        private void InitializeTaskbarProgress()
        {
            try
            {
                var taskbarService = App.GetService<TaskbarProgressService>();
                if (taskbarService != null)
                {
                    // 获取窗口句柄
                    var windowHandle = WindowNative.GetWindowHandle(this);
                    taskbarService.Initialize(windowHandle);
                }
            }
            catch
            {
                // 静默失败 - 任务栏进度是可选功能
            }
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
                    "Terminology" => typeof(TerminologyPage),
                    "TextEditor" => typeof(TextEditorPage),
                    "AssetExtraction" => typeof(AssetExtractionPage),
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

        /// <summary>
        /// 加载主题设置
        /// </summary>
        private async void LoadThemeSettings()
        {
            try
            {
                _isLoadingTheme = true;

                if (_configService != null)
                {
                    var config = await _configService.LoadConfigAsync();
                    string themeName = config.ApplicationTheme ?? "Default";

                    // 保存当前主题
                    _currentTheme = themeName;

                    // 更新导航项显示
                    UpdateThemeNavItem(themeName);

                    // 应用主题
                    ApplyTheme(themeName);
                }
            }
            catch
            {
                // 如果加载失败，使用默认主题
                _currentTheme = "Default";
                UpdateThemeNavItem("Default");
                ApplyTheme("Default");
            }
            finally
            {
                _isLoadingTheme = false;
            }
        }

        /// <summary>
        /// 主题切换导航项点击事件
        /// </summary>
        private async void ThemeNavItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // 如果正在加载主题，不触发保存
            if (_isLoadingTheme) return;

            // 循环切换主题：Light → Dark → Default → Light
            string nextTheme = _currentTheme switch
            {
                "Light" => "Dark",
                "Dark" => "Default",
                "Default" => "Light",
                _ => "Default"
            };

            _currentTheme = nextTheme;

            // 更新导航项显示
            UpdateThemeNavItem(nextTheme);

            // 应用主题
            ApplyTheme(nextTheme);

            // 保存到配置
            try
            {
                if (_configService != null)
                {
                    var config = await _configService.LoadConfigAsync();
                    config.ApplicationTheme = nextTheme;
                    await _configService.SaveConfigAsync(config);
                }
            }
            catch
            {
                // 静默失败
            }
        }

        /// <summary>
        /// 更新主题导航项显示
        /// </summary>
        private void UpdateThemeNavItem(string themeName)
        {
            // 定义主题对应的图标和文本
            (string glyph, string text, string tooltip) = themeName switch
            {
                "Light" => ("\uE706", "浅色", "当前：浅色模式 (点击切换)"),
                "Dark" => ("\uE708", "深色", "当前：深色模式 (点击切换)"),
                _ => ("\uE793", "跟随系统", "当前：跟随系统 (点击切换)")
            };

            // 更新NavigationViewItem的图标、文本和工具提示
            ThemeNavIcon.Glyph = glyph;
            ThemeNavItem.Content = text;
            ToolTipService.SetToolTip(ThemeNavItem, tooltip);
        }

        /// <summary>
        /// 应用主题
        /// </summary>
        private void ApplyTheme(string themeName)
        {
            ElementTheme theme = themeName switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            // 应用主题到窗口内容
            if (Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = theme;
            }
        }
    }
}
