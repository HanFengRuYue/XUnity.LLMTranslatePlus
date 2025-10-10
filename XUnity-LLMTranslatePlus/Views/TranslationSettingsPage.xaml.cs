using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Services;

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

        public TranslationSettingsPage()
        {
            this.InitializeComponent();

            _configService = App.GetService<ConfigService>();
            _logService = App.GetService<LogService>();

            LoadConfiguration();
        }

        private async void LoadConfiguration()
        {
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
            ContextWeightSlider.Value = config.ContextWeight;
            EnableCacheToggle.IsOn = config.EnableCache;

            // 自动检测翻译文件路径
            if (config.AutoDetectPath && !string.IsNullOrEmpty(config.GameDirectory))
            {
                DetectTranslationPath(config.GameDirectory);
            }
        }

        private AppConfig GetConfigFromUI()
        {
            var config = _currentConfig ?? new AppConfig();

            config.GameDirectory = GameDirectoryTextBox.Text;
            config.AutoDetectPath = AutoDetectPathToggle.IsOn;
            config.TargetLanguage = TargetLanguageComboBox.Text;
            config.SourceLanguage = SourceLanguageComboBox.Text;
            config.RealTimeMonitoring = RealTimeMonitoringToggle.IsOn;
            config.MonitorInterval = (int)MonitorIntervalNumberBox.Value;
            config.EnableContext = EnableContextToggle.IsOn;
            config.ContextLines = (int)ContextLinesNumberBox.Value;
            config.ContextWeight = ContextWeightSlider.Value;
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
        }

        private void EnableContextToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (ContextConfigPanel != null)
            {
                ContextConfigPanel.Visibility = EnableContextToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ContextWeightSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ContextWeightValueText != null)
            {
                ContextWeightValueText.Text = e.NewValue.ToString("F1");
            }
        }

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_configService == null)
            {
                return;
            }

            try
            {
                var config = GetConfigFromUI();
                await _configService.SaveConfigAsync(config);

                _logService?.Log("翻译设置已保存", LogLevel.Info);

                ContentDialog dialog = new ContentDialog
                {
                    Title = "保存成功",
                    Content = "翻译设置已保存！",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                _logService?.Log($"保存翻译设置失败: {ex.Message}", LogLevel.Error);

                ContentDialog dialog = new ContentDialog
                {
                    Title = "保存失败",
                    Content = $"保存失败: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
    }
}
