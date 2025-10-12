using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Services;
using Microsoft.UI.Dispatching;

namespace XUnity_LLMTranslatePlus.Views
{
    /// <summary>
    /// 翻译设置页面
    /// </summary>
    public sealed partial class TranslationSettingsPage : Page
    {
        private readonly ConfigService? _configService;
        private readonly LogService? _logService;
        private AppConfig? _currentConfig;
        private DispatcherQueueTimer? _autoSaveTimer;
        private bool _isLoadingConfig = false;

        public TranslationSettingsPage()
        {
            this.InitializeComponent();

            _configService = App.GetService<ConfigService>();
            _logService = App.GetService<LogService>();

            // 初始化自动保存定时器（防抖：500ms 后保存）
            _autoSaveTimer = DispatcherQueue.CreateTimer();
            _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _autoSaveTimer.Tick += (s, e) =>
            {
                _autoSaveTimer.Stop();
                _ = AutoSaveConfigAsync();
            };

            LoadConfiguration();
        }

        private async void LoadConfiguration()
        {
            _isLoadingConfig = true;
            try
            {
                if (_configService != null)
                {
                    _currentConfig = await _configService.LoadConfigAsync();
                    ApplyConfigToUI(_currentConfig);
                }
            }
            catch (Exception ex)
            {
                _logService?.Log($"加载翻译设置失败: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                _isLoadingConfig = false;
            }
        }

        private void ApplyConfigToUI(AppConfig config)
        {
            GameDirectoryTextBox.Text = config.GameDirectory;
            AutoDetectPathToggle.IsOn = config.AutoDetectPath;
            TargetLanguageComboBox.Text = config.TargetLanguage;
            SourceLanguageComboBox.Text = config.SourceLanguage;
            RealTimeMonitoringToggle.IsOn = config.RealTimeMonitoring;
            MonitorIntervalNumberBox.Value = config.MonitorInterval;
            EnableContextToggle.IsOn = config.EnableContext;
            ContextLinesNumberBox.Value = config.ContextLines;
            EnableCacheToggle.IsOn = config.EnableCache;

            // 自动检测翻译文件路径
            if (config.AutoDetectPath && !string.IsNullOrEmpty(config.GameDirectory))
            {
                DetectTranslationPath(config.GameDirectory);
            }
        }

        private async System.Threading.Tasks.Task<AppConfig> GetConfigFromUIAsync()
        {
            // 始终从 ConfigService 获取最新配置，确保不丢失其他页面的设置
            var config = await _configService!.LoadConfigAsync();

            // 只更新当前页面相关的字段
            config.GameDirectory = GameDirectoryTextBox.Text;
            config.AutoDetectPath = AutoDetectPathToggle.IsOn;
            config.TargetLanguage = TargetLanguageComboBox.Text;
            config.SourceLanguage = SourceLanguageComboBox.Text;
            config.RealTimeMonitoring = RealTimeMonitoringToggle.IsOn;
            config.MonitorInterval = (int)MonitorIntervalNumberBox.Value;
            config.EnableContext = EnableContextToggle.IsOn;
            config.ContextLines = (int)ContextLinesNumberBox.Value;
            config.EnableCache = EnableCacheToggle.IsOn;

            return config;
        }

        private async void BrowseGameDirButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            folderPicker.FileTypeFilter.Add("*");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                GameDirectoryTextBox.Text = folder.Path;

                if (AutoDetectPathToggle.IsOn)
                {
                    DetectTranslationPath(folder.Path);
                }

                // 自动保存
                TriggerAutoSave();
            }
        }

        private void DetectTranslationPath(string gameDir)
        {
            string translationPath = Path.Combine(gameDir, "BepInEx", "Translation");
            if (Directory.Exists(translationPath))
            {
                CurrentTranslationPathText.Text = translationPath;
                CurrentTranslationPathText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            }
            else
            {
                CurrentTranslationPathText.Text = "未找到 BepInEx/Translation 目录";
                CurrentTranslationPathText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }
        }

        private void RealTimeMonitoringToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (MonitorIntervalPanel != null)
            {
                MonitorIntervalPanel.Visibility = RealTimeMonitoringToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
            TriggerAutoSave();
        }

        private void EnableContextToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (ContextConfigPanel != null)
            {
                ContextConfigPanel.Visibility = EnableContextToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
            TriggerAutoSave();
        }

        /// <summary>
        /// 触发自动保存（带防抖）
        /// </summary>
        private void TriggerAutoSave()
        {
            if (_isLoadingConfig || _configService == null) return;

            // 重启定时器（防抖）
            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Start();
        }

        /// <summary>
        /// 自动保存配置
        /// </summary>
        private async System.Threading.Tasks.Task AutoSaveConfigAsync()
        {
            if (_configService == null) return;

            try
            {
                // 从 UI 获取配置（内部会加载完整配置并只更新翻译相关字段）
                var config = await GetConfigFromUIAsync();
                await _configService.SaveConfigAsync(config);

                // 更新本地缓存
                _currentConfig = config;

                _logService?.Log("翻译设置已自动保存", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logService?.Log($"自动保存翻译设置失败: {ex.Message}", LogLevel.Error);
            }
        }

        // 控件事件处理函数
        private void AutoDetectPathToggle_Toggled(object sender, RoutedEventArgs e)
        {
            TriggerAutoSave();
        }

        private void TargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TriggerAutoSave();
        }

        private void SourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TriggerAutoSave();
        }

        private void MonitorIntervalNumberBox_ValueChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
        {
            TriggerAutoSave();
        }

        private void ContextLinesNumberBox_ValueChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
        {
            TriggerAutoSave();
        }

        private void EnableCacheToggle_Toggled(object sender, RoutedEventArgs e)
        {
            TriggerAutoSave();
        }
    }
}
