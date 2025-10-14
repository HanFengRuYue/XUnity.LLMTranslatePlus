using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Windows.Storage;
using Windows.Storage.Pickers;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Services;

namespace XUnity_LLMTranslatePlus.Views
{
    // Bool to Visibility Converter
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }

    public sealed partial class TextEditorPage : Page
    {
        public ObservableCollection<TranslationEntry> FilteredEntries { get; set; } = new ObservableCollection<TranslationEntry>();

        private readonly TextEditorService? _textEditorService;
        private readonly ConfigService? _configService;
        private readonly LogService? _logService;
        private readonly FileMonitorService? _fileMonitorService;
        
        private TranslationEntry? currentEntry;
        private string originalValue = "";
        
        // 防抖定时器：避免短时间内多次重新加载文件
        private DispatcherQueueTimer? _reloadDebounceTimer;
        private readonly TimeSpan _reloadDebounceInterval = TimeSpan.FromMilliseconds(500);

        public TextEditorPage()
        {
            this.InitializeComponent();
            
            _textEditorService = App.GetService<TextEditorService>();
            _configService = App.GetService<ConfigService>();
            _logService = App.GetService<LogService>();
            _fileMonitorService = App.GetService<FileMonitorService>();

            if (_textEditorService != null)
            {
                _textEditorService.FileLoaded += OnFileLoaded;
                _textEditorService.EntriesUpdated += OnEntriesUpdated;
            }

            // 订阅翻译完成事件以实时更新
            if (_fileMonitorService != null)
            {
                _fileMonitorService.EntryTranslated += OnEntryTranslatedInMonitor;
                _fileMonitorService.StatusChanged += OnMonitorStatusChanged;
            }

            // 页面加载时自动加载文件
            this.Loaded += TextEditorPage_Loaded;
            this.Unloaded += TextEditorPage_Unloaded;

            // 初始化防抖定时器
            _reloadDebounceTimer = DispatcherQueue.CreateTimer();
            _reloadDebounceTimer.Interval = _reloadDebounceInterval;
            _reloadDebounceTimer.IsRepeating = false;
            _reloadDebounceTimer.Tick += async (s, e) =>
            {
                if (_textEditorService != null)
                {
                    await _textEditorService.ReloadAsync();
                }
            };
        }

        private void TextEditorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // 停止定时器
            _reloadDebounceTimer?.Stop();
        }

        private void OnMonitorStatusChanged(object? sender, FileMonitorEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateImportButtonState();
            });
        }

        private void UpdateImportButtonState()
        {
            if (ImportButton != null && _fileMonitorService != null)
            {
                ImportButton.IsEnabled = !_fileMonitorService.IsMonitoring;
            }
        }

        private async void TextEditorPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 自动加载翻译文件
            await AutoLoadFileAsync();
        }

        private async Task AutoLoadFileAsync()
        {
            if (_textEditorService == null || _configService == null) return;

            try
            {
                var config = _configService.GetCurrentConfig();

                // 快速返回：如果没有设置游戏目录，显示空状态提示
                if (string.IsNullOrWhiteSpace(config.GameDirectory))
                {
                    ShowEmptyState(true);
                    _logService?.Log("未设置游戏目录，请在翻译设置中配置", LogLevel.Info);
                    return;
                }

                // 添加超时保护（最多等待10秒）
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _textEditorService.LoadFromGameDirectoryAsync(config.GameDirectory);
            }
            catch (OperationCanceledException)
            {
                _logService?.Log("加载文件超时", LogLevel.Warning);
                ShowEmptyState(true);
            }
            catch (Exception ex)
            {
                _logService?.Log($"自动加载文件失败: {ex.Message}", LogLevel.Warning);
                ShowEmptyState(true);
            }
        }

        private void OnEntryTranslatedInMonitor(object? sender, TranslationEntry e)
        {
            // 监控服务翻译了新条目，使用防抖机制避免频繁重新加载
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_reloadDebounceTimer != null)
                {
                    // 重置定时器，500ms内没有新的翻译完成时才会重新加载
                    _reloadDebounceTimer.Stop();
                    _reloadDebounceTimer.Start();
                }
            });
        }

        private void OnFileLoaded(object? sender, string filePath)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // 显示文件路径
                if (!string.IsNullOrEmpty(filePath))
                {
                    EditorFilePathText.Text = filePath;
                    EditorFilePathBorder.Visibility = Visibility.Visible;
                }
                RefreshEntries();
                ShowEmptyState(false);
            });
        }

        private void OnEntriesUpdated(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RefreshEntries();
            });
        }

        private void RefreshEntries()
        {
            if (_textEditorService == null) return;

            var entries = _textEditorService.GetEntries();
            FilteredEntries.Clear();
            foreach (var entry in entries)
            {
                FilteredEntries.Add(entry);
            }

            var stats = _textEditorService.GetStatistics();
            if (stats != null)
            {
                TotalEntriesText.Text = stats.TotalEntries.ToString();
                TranslatedEntriesText.Text = stats.TranslatedEntries.ToString();
                UntranslatedEntriesText.Text = stats.UntranslatedEntries.ToString();
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_textEditorService == null) return;

            try
            {
                await _textEditorService.ReloadAsync();
            }
            catch (Exception ex)
            {
                _logService?.Log($"刷新失败: {ex.Message}", LogLevel.Error);
            }
        }

        private async void LoadFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_textEditorService == null || _configService == null) return;

            try
            {
                var config = _configService.GetCurrentConfig();
                if (string.IsNullOrEmpty(config.GameDirectory))
                {
                    ContentDialog dialog = new ContentDialog
                    {
                        Title = "提示",
                        Content = "请先在翻译设置页面配置游戏目录",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                    return;
                }

                await _textEditorService.LoadFromGameDirectoryAsync(config.GameDirectory);
            }
            catch (Exception ex)
            {
                _logService?.Log($"加载文件失败: {ex.Message}", LogLevel.Error);
                ContentDialog dialog = new ContentDialog
                {
                    Title = "错误",
                    Content = $"加载文件失败: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_textEditorService == null) return;

            string keyword = ""; // SearchTextBox.Text;
            var results = _textEditorService.SearchEntries(keyword);
            
            FilteredEntries.Clear();
            foreach (var entry in results)
            {
                FilteredEntries.Add(entry);
            }
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_textEditorService == null) return;
            var comboBox = sender as ComboBox;
            if (comboBox == null || comboBox.SelectedIndex < 0) return;

            var entries = comboBox.SelectedIndex switch
            {
                0 => _textEditorService.GetEntries(),
                1 => _textEditorService.FilterUntranslated(),
                2 => _textEditorService.FilterTranslated(),
                _ => _textEditorService.GetEntries()
            };

            FilteredEntries.Clear();
            foreach (var entry in entries)
            {
                FilteredEntries.Add(entry);
            }
        }

        private void EntryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listView = sender as ListView;
            if (listView?.SelectedItem is TranslationEntry entry)
            {
                currentEntry = entry;
                originalValue = entry.Value;
                
                OriginalTextBlock.Text = entry.Key;
                TranslatedTextBox.Text = entry.Value;
                
                ShowEmptyState(false);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_textEditorService == null || currentEntry == null) return;

            try
            {
                currentEntry.Value = TranslatedTextBox.Text;
                await _textEditorService.SaveEntryAsync(currentEntry);
                
                _logService?.Log("条目已保存", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _logService?.Log($"保存失败: {ex.Message}", LogLevel.Error);
            }
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentEntry != null)
            {
                TranslatedTextBox.Text = originalValue;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            TranslatedTextBox.Text = "";
        }

        private void ShowEmptyState(bool show)
        {
            if (EmptyStatePanel != null && EditorScrollViewer != null)
            {
                EmptyStatePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                EditorScrollViewer.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void CopyOriginalButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentEntry != null)
            {
                TranslatedTextBox.Text = currentEntry.Key;
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
        {
            // 文本搜索框内容改变事件
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput && _textEditorService != null)
            {
                string keyword = sender.Text;
                var results = _textEditorService.SearchEntries(keyword);
                
                FilteredEntries.Clear();
                foreach (var entry in results)
                {
                    FilteredEntries.Add(entry);
                }
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_textEditorService == null)
            {
                return;
            }

            // 检查是否正在翻译
            if (_fileMonitorService != null && _fileMonitorService.IsMonitoring)
            {
                ContentDialog warningDialog = new ContentDialog
                {
                    Title = "警告",
                    Content = "翻译服务正在运行，请先停止翻译再导入。",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await warningDialog.ShowAsync();
                return;
            }

            try
            {
                var picker = new FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add(".csv");
                picker.FileTypeFilter.Add(".txt");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    var csvContent = await FileIO.ReadTextAsync(file);
                    
                    using (var reader = new StringReader(csvContent))
                    using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        TrimOptions = TrimOptions.Trim
                    }))
                    {
                        var records = csv.GetRecords<CsvTranslationRecord>().ToList();
                        
                        // 准备批量保存的条目
                        var entriesToSave = new List<TranslationEntry>();
                        foreach (var record in records)
                        {
                            if (!string.IsNullOrEmpty(record.Original) && !string.IsNullOrEmpty(record.Translation))
                            {
                                entriesToSave.Add(new TranslationEntry
                                {
                                    Key = record.Original,
                                    Value = record.Translation,
                                    IsTranslated = !string.Equals(record.Original, record.Translation, StringComparison.Ordinal)
                                });
                            }
                        }

                        // 批量保存
                        await _textEditorService.SaveEntriesAsync(entriesToSave);

                        ContentDialog successDialog = new ContentDialog
                        {
                            Title = "导入成功",
                            Content = $"已成功导入 {entriesToSave.Count} 条翻译。",
                            CloseButtonText = "确定",
                            XamlRoot = this.XamlRoot
                        };
                        await successDialog.ShowAsync();

                        // 刷新显示
                        await _textEditorService.ReloadAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logService?.Log($"导入失败: {ex.Message}", LogLevel.Error);
                ContentDialog errorDialog = new ContentDialog
                {
                    Title = "导入失败",
                    Content = $"导入失败: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_textEditorService == null)
            {
                return;
            }

            try
            {
                // 询问导出范围
                ContentDialog optionDialog = new ContentDialog
                {
                    Title = "选择导出范围",
                    Content = "请选择要导出的内容：",
                    PrimaryButtonText = "全部",
                    SecondaryButtonText = "仅已翻译",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var result = await optionDialog.ShowAsync();
                if (result == ContentDialogResult.None)
                {
                    return;
                }

                bool exportOnlyTranslated = (result == ContentDialogResult.Secondary);

                var picker = new FileSavePicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("CSV 文件", new List<string> { ".csv" });
                picker.SuggestedFileName = $"translations_{DateTime.Now:yyyyMMdd_HHmmss}";

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    var allEntries = _textEditorService.GetEntries();
                    var entriesToExport = exportOnlyTranslated
                        ? allEntries.Where(e => e.IsTranslated).ToList()
                        : allEntries;

                    var csvRecords = entriesToExport.Select(e => new CsvTranslationRecord
                    {
                        Original = e.Key,
                        Translation = e.Value
                    }).ToList();

                    using (var writer = new StringWriter())
                    using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
                    {
                        csv.WriteRecords(csvRecords);
                        var csvContent = writer.ToString();
                        await FileIO.WriteTextAsync(file, csvContent);
                    }

                    ContentDialog successDialog = new ContentDialog
                    {
                        Title = "导出成功",
                        Content = $"已成功导出 {csvRecords.Count} 条翻译到 {file.Name}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                _logService?.Log($"导出失败: {ex.Message}", LogLevel.Error);
                ContentDialog errorDialog = new ContentDialog
                {
                    Title = "导出失败",
                    Content = $"导出失败: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        // CSV 记录类
        private class CsvTranslationRecord
        {
            public string Original { get; set; } = "";
            public string Translation { get; set; } = "";
        }
    }
}
