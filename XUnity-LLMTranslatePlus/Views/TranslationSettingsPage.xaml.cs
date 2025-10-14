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
            ManualFilePathTextBox.Text = config.ManualTranslationFilePath;
            TargetLanguageComboBox.Text = config.TargetLanguage;
            SourceLanguageComboBox.Text = config.SourceLanguage;
            RealTimeMonitoringToggle.IsOn = config.RealTimeMonitoring;
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
            config.ManualTranslationFilePath = ManualFilePathTextBox.Text;
            config.TargetLanguage = TargetLanguageComboBox.Text;
            config.SourceLanguage = SourceLanguageComboBox.Text;
            config.RealTimeMonitoring = RealTimeMonitoringToggle.IsOn;
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
            bool pathFound = Directory.Exists(translationPath);

            if (pathFound)
            {
                CurrentTranslationPathText.Text = translationPath;
                CurrentTranslationPathText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            }
            else
            {
                CurrentTranslationPathText.Text = "未找到 BepInEx/Translation 目录";
                CurrentTranslationPathText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }

            // 自动检测失败时显示手动指定选项
            UpdateManualFilePanelVisibility(!pathFound);
        }

        private void RealTimeMonitoringToggle_Toggled(object sender, RoutedEventArgs e)
        {
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

        /// <summary>
        /// 更新手动指定文件面板的可见性
        /// </summary>
        private void UpdateManualFilePanelVisibility(bool show)
        {
            if (ManualFilePanel != null)
            {
                ManualFilePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // 控件事件处理函数
        private void AutoDetectPathToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // 关闭自动检测时显示手动指定选项
            if (!AutoDetectPathToggle.IsOn)
            {
                UpdateManualFilePanelVisibility(true);
            }
            else
            {
                // 开启自动检测时，检查是否能找到路径
                if (!string.IsNullOrEmpty(GameDirectoryTextBox.Text))
                {
                    DetectTranslationPath(GameDirectoryTextBox.Text);
                }
                else
                {
                    UpdateManualFilePanelVisibility(false);
                }
            }

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

        private void ContextLinesNumberBox_ValueChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
        {
            TriggerAutoSave();
        }

        private void EnableCacheToggle_Toggled(object sender, RoutedEventArgs e)
        {
            TriggerAutoSave();
        }

        private async void BrowseManualFileButton_Click(object sender, RoutedEventArgs e)
        {
            var filePicker = new FileOpenPicker();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

            filePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            filePicker.FileTypeFilter.Add(".txt");

            var file = await filePicker.PickSingleFileAsync();
            if (file != null)
            {
                ManualFilePathTextBox.Text = file.Path;
                CurrentTranslationPathText.Text = $"手动指定: {file.Path}";
                CurrentTranslationPathText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];

                // 自动保存
                TriggerAutoSave();

                _logService?.Log($"手动指定翻译文件: {file.Path}", LogLevel.Info);
            }
        }

        private void ClearManualFileButton_Click(object sender, RoutedEventArgs e)
        {
            ManualFilePathTextBox.Text = "";

            // 恢复显示自动检测的路径
            if (AutoDetectPathToggle.IsOn && !string.IsNullOrEmpty(GameDirectoryTextBox.Text))
            {
                DetectTranslationPath(GameDirectoryTextBox.Text);
            }
            else
            {
                CurrentTranslationPathText.Text = "未检测到";
                CurrentTranslationPathText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            }

            // 自动保存
            TriggerAutoSave();

            _logService?.Log("已清除手动指定的翻译文件", LogLevel.Info);
        }
    }
}
