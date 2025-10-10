using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Services;

namespace XUnity_LLMTranslatePlus.Views
{
    public sealed partial class LogPage : Page
    {
        public class LogEntryDisplay
        {
            public string Timestamp { get; set; } = "";
            public string Level { get; set; } = "";
            public string Message { get; set; } = "";
            public string LevelColor { get; set; } = "";
        }

        public ObservableCollection<LogEntryDisplay> LogEntries { get; set; } = new ObservableCollection<LogEntryDisplay>();

        private readonly LogService? _logService;
        private readonly TranslationService? _translationService;
        private readonly FileMonitorService? _fileMonitorService;
        private LogLevel _currentFilter = LogLevel.Info;
        
        // 保存复选框状态（使用静态字段在整个应用生命周期内保持）
        private static bool _showDebug = true;
        private static bool _showInfo = true;
        private static bool _showWarning = true;
        private static bool _showError = true;
        
        // 标志：是否正在初始化（避免在加载时触发状态保存）
        private bool _isInitializing = false;

        public LogPage()
        {
            this.InitializeComponent();

            _logService = App.GetService<LogService>();
            _translationService = App.GetService<TranslationService>();
            _fileMonitorService = App.GetService<FileMonitorService>();
            
            if (_logService != null)
            {
                _logService.LogAdded += OnLogAdded;
                LoadExistingLogs();
            }

            // 订阅统计事件
            if (_translationService != null)
            {
                _translationService.ProgressUpdated += OnProgressUpdated;
            }

            if (_fileMonitorService != null)
            {
                _fileMonitorService.StatusChanged += OnMonitorStatusChanged;
            }

            // 初始化统计数据
            UpdateStatistics();

            // 页面加载时恢复复选框状态
            this.Loaded += LogPage_Loaded;
        }

        private void LogPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 设置初始化标志，避免在恢复状态时触发保存
            _isInitializing = true;
            
            // 恢复复选框状态
            if (DebugCheckBox != null) DebugCheckBox.IsChecked = _showDebug;
            if (InfoCheckBox != null) InfoCheckBox.IsChecked = _showInfo;
            if (WarningCheckBox != null) WarningCheckBox.IsChecked = _showWarning;
            if (ErrorCheckBox != null) ErrorCheckBox.IsChecked = _showError;
            
            // 完成初始化
            _isInitializing = false;
        }

        private void LoadExistingLogs()
        {
            if (_logService == null) return;

            var logs = _logService.GetRecentLogs();
            foreach (var log in logs)
            {
                AddLogEntry(log);
            }
        }

        private void OnLogAdded(object? sender, LogEntry log)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AddLogEntry(log);
            });
        }

        private void AddLogEntry(LogEntry log)
        {
            // 检查过滤器（使用复选框状态）
            if (!ShouldShowLogByCheckBox(log.Level))
            {
                return;
            }

            var display = new LogEntryDisplay
            {
                Timestamp = log.Timestamp,
                Level = log.Level,
                Message = log.Message,
                LevelColor = GetLevelColor(log.Level)
            };

            LogEntries.Insert(0, display);

            // 限制显示数量以提升性能（从1000降低到500）
            while (LogEntries.Count > 500)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }

            // 自动滚动到顶部（仅在启用自动滚动时）
            if (AutoScrollToggle?.IsOn == true && LogListView != null && LogListView.Items.Count > 0)
            {
                LogListView.ScrollIntoView(LogListView.Items[0]);
            }
        }

        private bool ShouldShowLog(string level)
        {
            var logLevel = Enum.TryParse<LogLevel>(level, out var parsed) ? parsed : LogLevel.Info;
            return logLevel >= _currentFilter;
        }

        private string GetLevelColor(string level)
        {
            return level switch
            {
                "Error" => "#E81123",
                "Warning" => "#FFC83D",
                "Info" => "#0078D4",
                "Debug" => "#8E8E93",
                _ => "#FFFFFF"
            };
        }

        private void LogLevelFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            if (comboBox == null || comboBox.SelectedIndex < 0) return;

            _currentFilter = comboBox.SelectedIndex switch
            {
                0 => LogLevel.Debug,
                1 => LogLevel.Info,
                2 => LogLevel.Warning,
                3 => LogLevel.Error,
                _ => LogLevel.Info
            };

            RefreshLogDisplay();
        }

        private void RefreshLogDisplay()
        {
            if (_logService == null) return;

            var logs = _logService.GetRecentLogs();
            var filteredLogs = logs
                .Where(log => ShouldShowLogByCheckBox(log.Level))
                .OrderByDescending(l => l.Timestamp)
                .Take(500) // 限制显示数量以提升性能
                .ToList();

            // 一次性更新，减少闪烁
            LogEntries.Clear();
            foreach (var log in filteredLogs)
            {
                var display = new LogEntryDisplay
                {
                    Timestamp = log.Timestamp,
                    Level = log.Level,
                    Message = log.Message,
                    LevelColor = GetLevelColor(log.Level)
                };
                LogEntries.Add(display);
            }
        }

        private bool ShouldShowLogByCheckBox(string level)
        {
            // 根据复选框状态判断是否显示该级别的日志
            return level switch
            {
                "Debug" => DebugCheckBox?.IsChecked == true,
                "Info" => InfoCheckBox?.IsChecked == true,
                "Warning" => WarningCheckBox?.IsChecked == true,
                "Error" => ErrorCheckBox?.IsChecked == true,
                _ => true
            };
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshLogDisplay();
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _logService?.ClearCachedLogs();
            LogEntries.Clear();
        }

        private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现暂停/恢复日志功能
        }

        private void LogFilterChanged(object sender, RoutedEventArgs e)
        {
            // 如果正在初始化，不保存状态（避免覆盖已保存的设置）
            if (!_isInitializing)
            {
                // 保存复选框状态
                if (DebugCheckBox != null) _showDebug = DebugCheckBox.IsChecked ?? true;
                if (InfoCheckBox != null) _showInfo = InfoCheckBox.IsChecked ?? true;
                if (WarningCheckBox != null) _showWarning = WarningCheckBox.IsChecked ?? true;
                if (ErrorCheckBox != null) _showError = ErrorCheckBox.IsChecked ?? true;
            }

            // 刷新显示
            RefreshLogDisplay();
        }

        private void OnProgressUpdated(object? sender, TranslationProgressEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateStatistics();
            });
        }

        private void OnMonitorStatusChanged(object? sender, FileMonitorEventArgs e)
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
                SuccessCountText.Text = _translationService.TotalTranslated.ToString();
                FailureCountText.Text = _translationService.TotalFailed.ToString();
            }

            if (_fileMonitorService != null)
            {
                QueueCountText.Text = _fileMonitorService.GetPendingCount().ToString();
                
                // 更新监控状态指示器
                if (_fileMonitorService.IsMonitoring)
                {
                    LogStatusText.Text = "运行中";
                    StatusIndicator.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                }
                else
                {
                    LogStatusText.Text = "已停止";
                    StatusIndicator.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                }
            }
        }

        private async void ExportLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_logService == null) return;

            try
            {
                var picker = new FileSavePicker();
                picker.FileTypeChoices.Add("文本文件", new[] { ".txt" });
                picker.SuggestedFileName = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}";

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await _logService.ExportLogsAsync(file.Path);

                    ContentDialog dialog = new ContentDialog
                    {
                        Title = "导出成功",
                        Content = $"日志已导出到: {file.Path}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "导出失败",
                    Content = $"导出失败: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
    }
}
