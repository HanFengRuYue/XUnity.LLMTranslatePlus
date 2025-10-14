using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Channels;
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
        private readonly HotkeyService? _hotkeyService;
        private DispatcherQueueTimer? _refreshTimer;

        // UI更新Channel（批量缓冲翻译条目以减少UI更新频率）
        private readonly Channel<TranslationEntry> _uiTranslationChannel;
        private DispatcherQueueTimer? _batchUpdateTimer;

        // 自动刷新配置的自动保存定时器
        private DispatcherQueueTimer? _autoRefreshSaveTimer;
        private bool _isLoadingAutoRefreshConfig = false;

        public HomePage()
        {
            this.InitializeComponent();
            RecentTranslations = _recentTranslationsCache;

            // 初始化UI更新Channel（无界通道）
            _uiTranslationChannel = Channel.CreateUnbounded<TranslationEntry>(new UnboundedChannelOptions
            {
                SingleReader = true,  // 只有UI线程读取
                SingleWriter = false  // 多个线程可能写入
            });

            // 获取服务
            _fileMonitorService = App.GetService<FileMonitorService>();
            _translationService = App.GetService<TranslationService>();
            _configService = App.GetService<ConfigService>();
            _logService = App.GetService<LogService>();
            _hotkeyService = App.GetService<HotkeyService>();

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

            // 启动自动刷新定时器（每2秒更新一次统计数据 - 优化4）
            _refreshTimer = DispatcherQueue.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(2);
            _refreshTimer.Tick += (s, e) => UpdateStatistics();
            _refreshTimer.Start();

            // 启动批量更新定时器（每500ms批量处理翻译条目）
            StartBatchUpdateTimer();

            // 初始化自动刷新保存定时器
            InitializeAutoRefreshSaveTimer();

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

            if (_batchUpdateTimer != null)
            {
                _batchUpdateTimer.Stop();
                _batchUpdateTimer = null;
            }

            if (_autoRefreshSaveTimer != null)
            {
                _autoRefreshSaveTimer.Stop();
                _autoRefreshSaveTimer = null;
            }

            // 停止自动刷新
            _hotkeyService?.StopAutoRefresh();

            _uiTranslationChannel.Writer.TryComplete();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 页面加载时同步后台监控状态
            SyncMonitoringState();

            // 加载自动刷新配置（必须在控件完全加载后才能访问）
            LoadAutoRefreshConfig();
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

                // 显示监控文件路径
                var filePath = _fileMonitorService.MonitoredFilePath;
                if (!string.IsNullOrEmpty(filePath))
                {
                    FilePathText.Text = filePath;
                    FilePathBorder.Visibility = Visibility.Visible;
                }

                // 启用自动刷新开关（修复：页面切换后按钮变灰的问题）
                AutoRefreshToggle.IsEnabled = true;

                // 如果配置中启用了自动刷新但服务未运行，则重新启动
                if (AutoRefreshToggle.IsOn && _hotkeyService != null && !_hotkeyService.IsRunning)
                {
                    _hotkeyService.StartAutoRefresh((int)AutoRefreshIntervalBox.Value, DispatcherQueue);
                }
            }
            else
            {
                // 确保UI显示已停止状态
                StartStopButton.Content = "开始翻译";
                StatusText.Text = "已停止";
                StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

                // 隐藏文件路径
                FilePathBorder.Visibility = Visibility.Collapsed;

                // 禁用自动刷新开关
                AutoRefreshToggle.IsEnabled = false;
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

        private void LoadAutoRefreshConfig()
        {
            if (_configService == null)
                return;

            _isLoadingAutoRefreshConfig = true;
            try
            {
                var config = _configService.GetCurrentConfig();
                AutoRefreshToggle.IsOn = config.EnableAutoRefresh;
                AutoRefreshIntervalBox.Value = config.AutoRefreshInterval;
            }
            finally
            {
                _isLoadingAutoRefreshConfig = false;
            }
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

                    // 显示文件路径
                    if (!string.IsNullOrEmpty(e.FilePath))
                    {
                        FilePathText.Text = e.FilePath;
                        FilePathBorder.Visibility = Visibility.Visible;
                    }

                    // 启用自动刷新开关
                    AutoRefreshToggle.IsEnabled = true;

                    // 如果配置中启用了自动刷新，则启动
                    if (AutoRefreshToggle.IsOn && _hotkeyService != null)
                    {
                        _hotkeyService.StartAutoRefresh((int)AutoRefreshIntervalBox.Value, DispatcherQueue);
                    }
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

                    // 隐藏文件路径
                    FilePathBorder.Visibility = Visibility.Collapsed;

                    // 禁用自动刷新开关并停止自动刷新
                    AutoRefreshToggle.IsEnabled = false;
                    _hotkeyService?.StopAutoRefresh();
                }
            });
        }

        private void OnEntryTranslated(object? sender, TranslationEntry e)
        {
            // 不再直接更新UI，而是写入Channel缓冲
            // 定时器会批量处理这些翻译条目，减少UI更新频率
            _uiTranslationChannel.Writer.TryWrite(e);
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
                // 翻译队列显示正在翻译中的数量（已发送给API等待返回）
                QueueCountText.Text = _translationService.TranslatingCount.ToString();
            }

            if (_fileMonitorService != null)
            {
                // 待翻译显示待处理的数量
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

        /// <summary>
        /// 启动批量更新定时器
        /// </summary>
        private void StartBatchUpdateTimer()
        {
            _batchUpdateTimer = DispatcherQueue.CreateTimer();
            _batchUpdateTimer.Interval = TimeSpan.FromMilliseconds(500); // 每500ms批量处理一次
            _batchUpdateTimer.Tick += BatchUpdateTimer_Tick;
            _batchUpdateTimer.Start();
        }

        /// <summary>
        /// 批量更新定时器触发
        /// </summary>
        private void BatchUpdateTimer_Tick(object? sender, object e)
        {
            BatchProcessTranslations();
        }

        /// <summary>
        /// 批量处理Channel中的翻译条目
        /// </summary>
        private void BatchProcessTranslations()
        {
            var batch = new List<TranslationEntry>();

            // 从Channel中读取最多20条翻译（避免单次处理过多）
            while (batch.Count < 20 && _uiTranslationChannel.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
            }

            if (batch.Count == 0)
            {
                return;
            }

            // 批量处理翻译条目
            foreach (var e in batch)
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
            }

            // 保持列表大小（限制为8条）
            while (RecentTranslations.Count > 8)
            {
                RecentTranslations.RemoveAt(RecentTranslations.Count - 1);
            }

            // 批量处理后统一更新一次统计
            UpdateStatistics();
        }

        #region 自动刷新配置

        /// <summary>
        /// 初始化自动刷新保存定时器
        /// </summary>
        private void InitializeAutoRefreshSaveTimer()
        {
            _autoRefreshSaveTimer = DispatcherQueue.CreateTimer();
            _autoRefreshSaveTimer.Interval = TimeSpan.FromMilliseconds(800);
            _autoRefreshSaveTimer.Tick += (s, e) =>
            {
                _autoRefreshSaveTimer?.Stop();
                _ = SaveAutoRefreshConfigAsync();
            };
        }

        /// <summary>
        /// 触发自动保存（防抖）
        /// </summary>
        private void TriggerAutoRefreshSave()
        {
            if (_isLoadingAutoRefreshConfig || _configService == null)
                return;

            _autoRefreshSaveTimer?.Stop();
            _autoRefreshSaveTimer?.Start();
        }

        /// <summary>
        /// 保存自动刷新配置
        /// </summary>
        private async System.Threading.Tasks.Task SaveAutoRefreshConfigAsync()
        {
            if (_configService == null) return;

            try
            {
                // 加载最新配置
                await _configService.LoadConfigAsync();
                var config = _configService.GetCurrentConfig();

                // 更新自动刷新配置
                config.EnableAutoRefresh = AutoRefreshToggle.IsOn;
                config.AutoRefreshInterval = (int)AutoRefreshIntervalBox.Value;

                // 保存配置
                await _configService.SaveConfigAsync(config);
                _logService?.Log("自动刷新配置已保存", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logService?.Log($"保存自动刷新配置失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 自动刷新开关切换事件
        /// </summary>
        private void AutoRefreshToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoadingAutoRefreshConfig)
                return;

            TriggerAutoRefreshSave();

            // 根据开关状态启动或停止自动刷新
            if (AutoRefreshToggle.IsOn)
            {
                if (_fileMonitorService?.IsMonitoring == true && _hotkeyService != null)
                {
                    _hotkeyService.StartAutoRefresh((int)AutoRefreshIntervalBox.Value, DispatcherQueue);
                }
            }
            else
            {
                _hotkeyService?.StopAutoRefresh();
            }
        }

        /// <summary>
        /// 自动刷新间隔修改事件
        /// </summary>
        private void AutoRefreshIntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isLoadingAutoRefreshConfig)
                return;

            // 验证范围
            if (args.NewValue < 1 || args.NewValue > 60)
                return;

            TriggerAutoRefreshSave();

            // 如果自动刷新正在运行，重启以应用新间隔
            if (AutoRefreshToggle.IsOn && _hotkeyService?.IsRunning == true)
            {
                _hotkeyService.StopAutoRefresh();
                _hotkeyService.StartAutoRefresh((int)args.NewValue, DispatcherQueue);
            }
        }

        #endregion
    }
}

