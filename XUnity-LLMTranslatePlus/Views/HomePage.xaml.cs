using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Services;

namespace XUnity_LLMTranslatePlus.Views
{
    /// <summary>
    /// 主页
    /// </summary>
    public sealed partial class HomePage : Page
    {
        // 使用静态集合保存最近翻译，避免页面重新创建时丢失
        private static ObservableCollection<TranslationEntry> _recentTranslationsCache = new();
        public ObservableCollection<TranslationEntry> RecentTranslations { get; set; }

        private readonly FileMonitorService? _fileMonitorService;
        private readonly TranslationService? _translationService;
        private readonly ConfigService? _configService;
        private readonly LogService? _logService;
        private DispatcherQueueTimer? _refreshTimer;

        public HomePage()
        {
            this.InitializeComponent();
            RecentTranslations = _recentTranslationsCache;

            // 获取服务
            _fileMonitorService = App.GetService<FileMonitorService>();
            _translationService = App.GetService<TranslationService>();
            _configService = App.GetService<ConfigService>();
            _logService = App.GetService<LogService>();

            // 订阅事件
            if (_fileMonitorService != null)
            {
                _fileMonitorService.StatusChanged += OnMonitorStatusChanged;
                _fileMonitorService.EntryTranslated += OnEntryTranslated;
                _fileMonitorService.ErrorThresholdReached += OnErrorThresholdReached;
            }

            if (_translationService != null)
            {
                _translationService.ProgressUpdated += OnTranslationProgressUpdated;
            }

            // 初始化界面
            InitializeUI();

            // 启动自动刷新定时器（每秒更新一次统计数据）
            _refreshTimer = DispatcherQueue.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(1);
            _refreshTimer.Tick += (s, e) => UpdateStatistics();
            _refreshTimer.Start();

            // 订阅页面卸载事件
            this.Unloaded += HomePage_Unloaded;
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            // 停止定时器，避免在应用关闭后继续更新UI
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer = null;
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 页面加载时同步后台监控状态
            SyncMonitoringState();
        }

        private void SyncMonitoringState()
        {
            if (_fileMonitorService == null) return;

            // 检查后台是否正在监控
            if (_fileMonitorService.IsMonitoring)
            {
                // 更新UI以反映监控状态
                StartStopButton.Content = "停止翻译";
                StatusText.Text = "运行中";
                StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];

                StatusInfoBar.Severity = InfoBarSeverity.Success;
                StatusInfoBar.Title = "翻译服务运行中";
                StatusInfoBar.Message = "正在后台自动翻译";
                StatusInfoBar.IsOpen = true;
            }
            else
            {
                // 确保UI显示已停止状态
                StartStopButton.Content = "开始翻译";
                StatusText.Text = "已停止";
                StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }

            // 更新统计信息
            UpdateStatistics();
        }

        private async void InitializeUI()
        {
            // 加载配置
            if (_configService != null)
            {
                await _configService.LoadConfigAsync();
            }

            UpdateStatistics();
        }

        private async void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_fileMonitorService == null || _configService == null)
            {
                ShowError("服务未初始化");
                return;
            }

            try
            {
                if (_fileMonitorService.IsMonitoring)
                {
                    // 停止翻译
                    await _fileMonitorService.StopMonitoringAsync();
                }
                else
                {
                    // 开始翻译
                    var config = _configService.GetCurrentConfig();

                    if (string.IsNullOrEmpty(config.GameDirectory))
                    {
                        ShowError("请先在翻译设置页面配置游戏目录");
                        return;
                    }

                    _translationService?.ResetStatistics();
                    await _fileMonitorService.StartMonitoringAsync(config.GameDirectory);
                }
            }
            catch (Exception ex)
            {
                ShowError($"操作失败: {ex.Message}");
                _logService?.Log($"监控操作失败: {ex.Message}", LogLevel.Error);
            }
        }

        private void OnMonitorStatusChanged(object? sender, FileMonitorEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (e.IsMonitoring)
                {
                    StartStopButton.Content = "停止翻译";
                    StatusText.Text = "运行中";
                    StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];

                    StatusInfoBar.Severity = InfoBarSeverity.Success;
                    StatusInfoBar.Title = "翻译服务已启动";
                    StatusInfoBar.Message = $"正在翻译: {e.FilePath}";
                    StatusInfoBar.IsOpen = true;
                }
                else
                {
                    StartStopButton.Content = "开始翻译";
                    StatusText.Text = "已停止";
                    StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

                    StatusInfoBar.Severity = InfoBarSeverity.Informational;
                    StatusInfoBar.Title = "翻译服务已停止";
                    StatusInfoBar.Message = "点击\"开始翻译\"按钮开始翻译。";
                    StatusInfoBar.IsOpen = true;
                }
            });
        }

        private void OnEntryTranslated(object? sender, TranslationEntry e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // 检查是否已经存在相同的翻译（防止重复显示）
                var existingEntry = RecentTranslations.FirstOrDefault(t => 
                    t.Key == e.Key && t.Value == e.Value);
                
                if (existingEntry != null)
                {
                    // 如果已存在，移除旧的，添加到顶部（更新时间）
                    RecentTranslations.Remove(existingEntry);
                }

                // 添加到最近翻译列表
                RecentTranslations.Insert(0, e);

                // 保持列表大小
                while (RecentTranslations.Count > 10)
                {
                    RecentTranslations.RemoveAt(RecentTranslations.Count - 1);
                }

                UpdateStatistics();
            });
        }

        private void OnTranslationProgressUpdated(object? sender, TranslationProgressEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateStatistics();
            });
        }

        private void UpdateStatistics()
        {
            if (_translationService != null)
            {
                TranslatedCountText.Text = _translationService.TotalTranslated.ToString();
                ErrorCountText.Text = _translationService.TotalFailed.ToString();
            }

            if (_fileMonitorService != null)
            {
                PendingCountText.Text = _fileMonitorService.GetPendingCount().ToString();
            }
        }

        private async void OpenGameDirButton_Click(object sender, RoutedEventArgs e)
        {
            var config = _configService?.GetCurrentConfig();

            if (config == null || string.IsNullOrEmpty(config.GameDirectory))
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "打开游戏目录",
                    Content = "请先在翻译设置页面配置游戏目录。",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", config.GameDirectory);
            }
            catch (Exception ex)
            {
                ShowError($"打开目录失败: {ex.Message}");
            }
        }

        private void ShowError(string message)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Title = "错误";
            StatusInfoBar.Message = message;
            StatusInfoBar.IsOpen = true;
        }

        private void OnErrorThresholdReached(object? sender, string message)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                ShowError(message);

                ContentDialog dialog = new ContentDialog
                {
                    Title = "错误过多，已停止翻译",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            });
        }
    }
}

