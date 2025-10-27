using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Services;

namespace XUnity_LLMTranslatePlus.Views
{
    public sealed partial class AssetExtractionPage : Page
    {
        private readonly AssetScannerService? _scannerService;
        private readonly PreTranslationService? _preTranslationService;
        private readonly ConfigService? _configService;
        private readonly LogService? _logService;

        private List<ExtractedTextEntry> _extractedTexts = new List<ExtractedTextEntry>();
        private List<ExtractedTextEntry> _filteredTexts = new List<ExtractedTextEntry>();
        private ObservableCollection<FieldNameEntry> _fieldNames = new ObservableCollection<FieldNameEntry>();
        private ObservableCollection<FieldNameEntry> _excludeFieldNames = new ObservableCollection<FieldNameEntry>();
        private ObservableCollection<PatternEntry> _excludePatterns = new ObservableCollection<PatternEntry>();
        private CancellationTokenSource? _cancellationTokenSource;

        // 自动保存定时器
        private DispatcherQueueTimer? _autoSaveTimer;
        private bool _isLoadingConfig = false;

        public AssetExtractionPage()
        {
            this.InitializeComponent();

            // 启用页面缓存，保留状态
            this.NavigationCacheMode = NavigationCacheMode.Required;

            // 从依赖注入容器获取服务
            _scannerService = App.GetService<AssetScannerService>();
            _preTranslationService = App.GetService<PreTranslationService>();
            _configService = App.GetService<ConfigService>();
            _logService = App.GetService<LogService>();

            // 初始化自动保存定时器
            _autoSaveTimer = DispatcherQueue.CreateTimer();
            _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(800);
            _autoSaveTimer.Tick += (s, e) =>
            {
                _autoSaveTimer.Stop();
                _ = AutoSaveConfigAsync();
            };

            Loaded += AssetExtractionPage_Loaded;
        }

        private async void AssetExtractionPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_configService != null)
            {
                var config = await _configService.LoadConfigAsync();
                await LoadConfigToUIAsync(config);
            }

            if (_logService != null)
            {
                await _logService.LogAsync("资产提取页面已加载", LogLevel.Info);
            }
        }

        private async Task LoadConfigToUIAsync(AppConfig config)
        {
            _isLoadingConfig = true;

            try
            {
                // 加载扫描设置
                ScanTextAssetsCheckBox.IsChecked = config.AssetExtraction.ScanTextAssets;
                ScanMonoBehavioursCheckBox.IsChecked = config.AssetExtraction.ScanMonoBehaviours;
                ClassDatabasePathTextBox.Text = config.AssetExtraction.ClassDatabasePath;

                // 加载语言过滤器
                var languageIndex = config.AssetExtraction.SourceLanguageFilter switch
                {
                    "全部语言" => 0,
                    "中日韩（CJK）" => 1,
                    "简体中文" => 2,
                    "繁体中文" => 3,
                    "日语" => 4,
                    "英语" => 5,
                    "韩语" => 6,
                    "俄语" => 7,
                    _ => 0
                };
                SourceLanguageComboBox.SelectedIndex = languageIndex;

                // 加载扫描模式
                if (config.AssetExtraction.ScanMode == MonoBehaviourScanMode.SpecifiedFields)
                {
                    SpecifiedFieldsRadioButton.IsChecked = true;
                }
                else
                {
                    AllStringFieldsRadioButton.IsChecked = true;
                }

                // 加载递归深度
                RecursionDepthSlider.Value = config.AssetExtraction.MaxRecursionDepth;
                RecursionDepthText.Text = config.AssetExtraction.MaxRecursionDepth.ToString();

                // 加载字段名列表
                _fieldNames.Clear();
                foreach (var field in config.AssetExtraction.MonoBehaviourFields)
                {
                    _fieldNames.Add(new FieldNameEntry { FieldName = field });
                }
                FieldNamesListView.ItemsSource = _fieldNames;
                UpdateFieldCountText();

                // 加载排除字段名列表
                _excludeFieldNames.Clear();
                foreach (var field in config.AssetExtraction.ExcludeFieldNames)
                {
                    _excludeFieldNames.Add(new FieldNameEntry { FieldName = field });
                }
                ExcludeFieldNamesListView.ItemsSource = _excludeFieldNames;
                UpdateExcludeFieldCountText();

                // 加载排除模式列表
                _excludePatterns.Clear();
                foreach (var pattern in config.AssetExtraction.ExcludePatterns)
                {
                    _excludePatterns.Add(new PatternEntry { Pattern = pattern });
                }
                ExcludePatternsListView.ItemsSource = _excludePatterns;
                UpdateExcludePatternCountText();

                // 根据扫描模式更新 UI 显示
                UpdateScanModeUI();
            }
            finally
            {
                _isLoadingConfig = false;
            }
        }

        private void TriggerAutoSave()
        {
            if (_isLoadingConfig || _configService == null) return;
            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Start();
        }

        private async Task AutoSaveConfigAsync()
        {
            if (_configService == null) return;

            try
            {
                var config = await _configService.LoadConfigAsync();

                // 更新配置
                config.AssetExtraction.ScanTextAssets = ScanTextAssetsCheckBox.IsChecked ?? true;
                config.AssetExtraction.ScanMonoBehaviours = ScanMonoBehavioursCheckBox.IsChecked ?? true;
                config.AssetExtraction.ClassDatabasePath = ClassDatabasePathTextBox.Text;
                config.AssetExtraction.SourceLanguageFilter = ((ComboBoxItem)SourceLanguageComboBox.SelectedItem)?.Content?.ToString() ?? "全部语言";

                // 更新扫描模式
                config.AssetExtraction.ScanMode = SpecifiedFieldsRadioButton.IsChecked == true
                    ? MonoBehaviourScanMode.SpecifiedFields
                    : MonoBehaviourScanMode.AllStringFields;

                // 更新递归深度
                config.AssetExtraction.MaxRecursionDepth = (int)RecursionDepthSlider.Value;

                // 更新字段名列表
                config.AssetExtraction.MonoBehaviourFields = _fieldNames
                    .Where(f => !string.IsNullOrWhiteSpace(f.FieldName))
                    .Select(f => f.FieldName)
                    .Distinct()
                    .ToList();

                // 更新排除字段名列表
                config.AssetExtraction.ExcludeFieldNames = _excludeFieldNames
                    .Where(f => !string.IsNullOrWhiteSpace(f.FieldName))
                    .Select(f => f.FieldName)
                    .Distinct()
                    .ToList();

                // 更新排除模式列表
                config.AssetExtraction.ExcludePatterns = _excludePatterns
                    .Where(p => !string.IsNullOrWhiteSpace(p.Pattern))
                    .Select(p => p.Pattern)
                    .Distinct()
                    .ToList();

                await _configService.SaveConfigAsync(config);
            }
            catch (Exception ex)
            {
                if (_logService != null)
                {
                    await _logService.LogAsync($"保存配置失败: {ex.Message}", LogLevel.Error);
                }
            }
        }

        private void SourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TriggerAutoSave();
        }

        // ==================== 字段名管理 ====================

        private void AddFieldButton_Click(object sender, RoutedEventArgs e)
        {
            _fieldNames.Add(new FieldNameEntry { FieldName = "" });
            UpdateFieldCountText();
            TriggerAutoSave();
        }

        private void DeleteFieldButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = FieldNamesListView.SelectedItems.Cast<FieldNameEntry>().ToList();
            foreach (var item in selectedItems)
            {
                _fieldNames.Remove(item);
            }
            UpdateFieldCountText();
            TriggerAutoSave();
        }

        private void FieldNamesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteFieldButton.IsEnabled = FieldNamesListView.SelectedItems.Count > 0;
        }

        private void FieldNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TriggerAutoSave();
        }

        private void UpdateFieldCountText()
        {
            var count = _fieldNames.Count;
            FieldCountText.Text = count > 0 ? $"共 {count} 个字段" : "";
        }

        // ==================== 扫描模式切换 ====================

        private void ScanModeRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingConfig) return;

            UpdateScanModeUI();
            TriggerAutoSave();
        }

        private void UpdateScanModeUI()
        {
            bool isSpecifiedFields = SpecifiedFieldsRadioButton?.IsChecked == true;

            // 根据扫描模式显示/隐藏对应的 UI
            if (FieldNamesExpander != null)
            {
                FieldNamesExpander.Visibility = isSpecifiedFields ? Visibility.Visible : Visibility.Collapsed;
            }

            if (AdvancedFilterExpander != null)
            {
                AdvancedFilterExpander.Visibility = isSpecifiedFields ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // ==================== 高级过滤设置 ====================

        private void RecursionDepthSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isLoadingConfig) return;

            int depth = (int)e.NewValue;
            if (RecursionDepthText != null)
            {
                RecursionDepthText.Text = depth.ToString();
            }
            TriggerAutoSave();
        }

        private void AddExcludeFieldButton_Click(object sender, RoutedEventArgs e)
        {
            _excludeFieldNames.Add(new FieldNameEntry { FieldName = "" });
            UpdateExcludeFieldCountText();
            TriggerAutoSave();
        }

        private void DeleteExcludeFieldButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ExcludeFieldNamesListView.SelectedItems.Cast<FieldNameEntry>().ToList();
            foreach (var item in selectedItems)
            {
                _excludeFieldNames.Remove(item);
            }
            UpdateExcludeFieldCountText();
            TriggerAutoSave();
        }

        private void ExcludeFieldNamesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteExcludeFieldButton.IsEnabled = ExcludeFieldNamesListView.SelectedItems.Count > 0;
        }

        private void ExcludeFieldNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TriggerAutoSave();
        }

        private void UpdateExcludeFieldCountText()
        {
            var count = _excludeFieldNames.Count;
            ExcludeFieldCountText.Text = count > 0 ? $"共 {count} 个字段" : "";
        }

        // ==================== 排除模式管理 ====================

        private void AddExcludePatternButton_Click(object sender, RoutedEventArgs e)
        {
            _excludePatterns.Add(new PatternEntry { Pattern = "" });
            UpdateExcludePatternCountText();
            TriggerAutoSave();
        }

        private void DeleteExcludePatternButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ExcludePatternsListView.SelectedItems.Cast<PatternEntry>().ToList();
            foreach (var item in selectedItems)
            {
                _excludePatterns.Remove(item);
            }
            UpdateExcludePatternCountText();
            TriggerAutoSave();
        }

        private void ExcludePatternsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteExcludePatternButton.IsEnabled = ExcludePatternsListView.SelectedItems.Count > 0;
        }

        private void ExcludePatternTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TriggerAutoSave();
        }

        private void UpdateExcludePatternCountText()
        {
            var count = _excludePatterns.Count;
            ExcludePatternCountText.Text = count > 0 ? $"共 {count} 个模式" : "";
        }

        // ==================== 扫描和翻译 ====================

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_scannerService == null || _configService == null || _logService == null)
            {
                return;
            }

            var config = await _configService.LoadConfigAsync();

            if (string.IsNullOrWhiteSpace(config.GameDirectory))
            {
                await _logService.LogAsync("请先在翻译设置中配置游戏目录", LogLevel.Error);
                return;
            }

            try
            {
                // 保存当前配置
                await AutoSaveConfigAsync();

                // UI 状态
                SetUIBusy(true);
                _cancellationTokenSource = new CancellationTokenSource();

                await _logService.LogAsync("开始扫描资产文件...", LogLevel.Info);

                // 使用 Task.Run 在后台线程执行扫描
                var scanResult = await Task.Run(async () =>
                {
                    try
                    {
                        var assetFiles = await _scannerService.ScanAssetsAsync(
                            config.GameDirectory,
                            config.AssetExtraction,
                            new Progress<AssetScanProgress>(p =>
                            {
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    ProgressBar.IsIndeterminate = false;
                                    ProgressBar.Value = p.ProgressPercentage;
                                    ProgressPanel.Visibility = Visibility.Visible;
                                    ProgressText.Text = $"扫描资产文件... {p.ProcessedAssets}/{p.TotalAssets}";
                                });
                            }),
                            _cancellationTokenSource.Token);

                        return assetFiles;
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                }, _cancellationTokenSource.Token);

                if (scanResult == null || _cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await _logService.LogAsync("扫描操作已取消", LogLevel.Warning);
                    return;
                }

                await _logService.LogAsync($"扫描完成，找到 {scanResult.Count} 个资产文件", LogLevel.Info);

                if (scanResult.Count == 0)
                {
                    await _logService.LogAsync("未找到资产文件", LogLevel.Warning);
                    return;
                }

                // 提取文本
                await _logService.LogAsync("开始提取文本...", LogLevel.Info);
                ProgressText.Text = "正在提取文本...";
                ProgressBar.IsIndeterminate = true;

                var extractResult = await Task.Run(async () =>
                {
                    try
                    {
                        using var extractor = new AssetTextExtractor(_logService, config.AssetExtraction);
                        var texts = await extractor.ExtractTextsAsync(
                            config.GameDirectory,
                            scanResult,
                            new Progress<AssetScanProgress>(p =>
                            {
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    ProgressBar.IsIndeterminate = false;
                                    ProgressBar.Value = p.ProgressPercentage;
                                    ProgressText.Text = $"提取文本... {p.ProcessedAssets}/{p.TotalAssets} ({p.ExtractedTexts} 条文本)";
                                });
                            }),
                            _cancellationTokenSource.Token);

                        // 去重
                        return extractor.FilterAndDeduplicateTexts(texts);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                }, _cancellationTokenSource.Token);

                if (extractResult == null || _cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await _logService.LogAsync("提取操作已取消", LogLevel.Warning);
                    return;
                }

                _extractedTexts = extractResult;
                await _logService.LogAsync($"提取完成，共 {_extractedTexts.Count} 条唯一文本", LogLevel.Info);

                // 更新 UI
                DispatcherQueue.TryEnqueue(() =>
                {
                    StatsGrid.Visibility = Visibility.Visible;
                    AssetCountText.Text = scanResult.Count.ToString();
                    ExtractedTextCountText.Text = _extractedTexts.Count.ToString();

                    UpdateExtractedTextsList();
                    TranslateButton.IsEnabled = _extractedTexts.Count > 0;
                });
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"扫描失败: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
                SetUIBusy(false);
            }
        }

        private async void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_preTranslationService == null || _configService == null || _logService == null || _extractedTexts.Count == 0)
            {
                return;
            }

            // 获取选中的文本
            var selectedTexts = ExtractedTextsListView.SelectedItems.Cast<ExtractedTextEntry>().ToList();
            if (selectedTexts.Count == 0)
            {
                await _logService.LogAsync("未选择任何文本进行翻译", LogLevel.Warning);
                return;
            }

            var config = await _configService.LoadConfigAsync();

            try
            {
                SetUIBusy(true);
                _cancellationTokenSource = new CancellationTokenSource();

                await _logService.LogAsync($"开始预翻译流程... (共 {selectedTexts.Count} 条)", LogLevel.Info);

                var result = await _preTranslationService.ExecutePreTranslationAsync(
                    config.GameDirectory,
                    selectedTexts,
                    new Progress<PreTranslationProgress>(p =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ProgressBar.IsIndeterminate = false;
                            ProgressBar.Value = p.ProgressPercentage;
                            ProgressPanel.Visibility = Visibility.Visible;
                            ProgressText.Text = p.CurrentStep;
                        });
                    }),
                    _cancellationTokenSource.Token);

                if (result.Success)
                {
                    await _logService.LogAsync($"翻译完成: 成功 {result.SuccessfulTranslations} 条，失败 {result.FailedTranslations} 条", LogLevel.Info);
                    TranslatedCountText.Text = result.SuccessfulTranslations.ToString();
                    FailedCountText.Text = result.FailedTranslations.ToString();
                }
                else
                {
                    await _logService.LogAsync($"预翻译失败: {result.ErrorMessage}", LogLevel.Error);
                }
            }
            catch (OperationCanceledException)
            {
                await _logService.LogAsync("翻译操作已取消", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"翻译失败: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
                SetUIBusy(false);
            }
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            if (_logService != null)
            {
                await _logService.LogAsync("正在取消操作...", LogLevel.Warning);
            }
        }

        private void SetUIBusy(bool isBusy)
        {
            ScanButton.IsEnabled = !isBusy;
            TranslateButton.IsEnabled = !isBusy && _extractedTexts.Count > 0;
            CancelButton.IsEnabled = isBusy;
        }

        // ==================== ClassDatabase 文件选择 ====================

        private async void BrowseClassDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".tpk");
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

                var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    ClassDatabasePathTextBox.Text = file.Path;
                    if (_logService != null)
                    {
                        await _logService.LogAsync($"已选择 ClassDatabase 文件: {file.Name}", LogLevel.Info);
                    }
                    TriggerAutoSave();
                }
            }
            catch (Exception ex)
            {
                if (_logService != null)
                {
                    await _logService.LogAsync($"选择文件失败: {ex.Message}", LogLevel.Error);
                }
            }
        }

        private async void ClearClassDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            ClassDatabasePathTextBox.Text = "";
            if (_logService != null)
            {
                await _logService.LogAsync("已清除 ClassDatabase 文件路径，将使用程序内置版本", LogLevel.Info);
            }
            TriggerAutoSave();
        }

        // ==================== 文本列表管理 ====================

        private void UpdateExtractedTextsList()
        {
            var searchText = SearchTextBox?.Text ?? "";
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _filteredTexts = _extractedTexts.ToList();
            }
            else
            {
                _filteredTexts = _extractedTexts
                    .Where(t => t.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                t.RelativeSourcePath.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            ExtractedTextsListView.ItemsSource = _filteredTexts;
            ExtractedTextsPanel.Visibility = _extractedTexts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            UpdateExtractedTextsStats();
        }

        private void UpdateExtractedTextsStats()
        {
            var totalCount = _extractedTexts.Count;
            var filteredCount = _filteredTexts.Count;
            var selectedCount = ExtractedTextsListView.SelectedItems.Count;

            if (totalCount == filteredCount)
            {
                ExtractedTextsStats.Text = $"共 {totalCount} 条文本，已选择 {selectedCount} 条";
            }
            else
            {
                ExtractedTextsStats.Text = $"共 {totalCount} 条文本，显示 {filteredCount} 条，已选择 {selectedCount} 条";
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateExtractedTextsList();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            ExtractedTextsListView.SelectAll();
            UpdateExtractedTextsStats();
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            ExtractedTextsListView.SelectedItems.Clear();
            UpdateExtractedTextsStats();
        }

        private void ExtractedTextsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateExtractedTextsStats();
        }

        // ==================== 右键菜单功能 ====================

        private void CopyTextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is ExtractedTextEntry entry)
            {
                CopyToClipboard(entry.Text);
            }
        }

        private void CopyFieldPathMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is ExtractedTextEntry entry)
            {
                CopyToClipboard(entry.FieldName);
            }
        }

        private void CopySourcePathMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is ExtractedTextEntry entry)
            {
                CopyToClipboard(entry.SourceAsset);
            }
        }

        private async void AddFieldToBlacklistMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is ExtractedTextEntry entry)
            {
                if (string.IsNullOrWhiteSpace(entry.FieldName))
                {
                    await ShowNotificationAsync("字段名为空，无法添加到黑名单", false);
                    return;
                }

                // 提取字段名（去除路径，只保留最后一级）
                string fieldName = entry.FieldName.Contains('.')
                    ? entry.FieldName.Substring(entry.FieldName.LastIndexOf('.') + 1)
                    : entry.FieldName;

                // 检查是否已存在
                if (_excludeFieldNames.Any(f => f.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
                {
                    await ShowNotificationAsync($"字段名 '{fieldName}' 已在黑名单中", false);
                    return;
                }

                // 添加到黑名单
                _excludeFieldNames.Add(new FieldNameEntry { FieldName = fieldName });
                UpdateExcludeFieldCountText();
                TriggerAutoSave();

                await ShowNotificationAsync($"已添加 '{fieldName}' 到字段名黑名单，下次扫描生效", true);
            }
        }

        private async void AddToExcludePatternMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is ExtractedTextEntry entry)
            {
                if (string.IsNullOrWhiteSpace(entry.Text))
                {
                    await ShowNotificationAsync("文本为空，无法生成排除模式", false);
                    return;
                }

                // 生成智能正则表达式
                string pattern = GenerateExcludePattern(entry.Text);

                // 检查是否已存在
                if (_excludePatterns.Any(p => p.Pattern == pattern))
                {
                    await ShowNotificationAsync("该排除模式已存在", false);
                    return;
                }

                // 添加到 UI 列表
                _excludePatterns.Add(new PatternEntry { Pattern = pattern });
                UpdateExcludePatternCountText();
                TriggerAutoSave();

                await ShowNotificationAsync($"已添加排除模式: {pattern}\n下次扫描生效", true);
            }
        }

        /// <summary>
        /// 复制文本到剪贴板
        /// </summary>
        private void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            }
            catch (Exception ex)
            {
                if (_logService != null)
                {
                    _ = _logService.LogAsync($"复制到剪贴板失败: {ex.Message}", LogLevel.Warning);
                }
            }
        }

        /// <summary>
        /// 智能生成排除正则表达式
        /// </summary>
        private string GenerateExcludePattern(string text)
        {
            var trimmed = text.Trim();

            // JSON 对象
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                return @"^\s*\{.*\}\s*$";

            // JSON 数组
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                return @"^\s*\[.*\]\s*$";

            // 数字坐标/数组（如 "0.5,1.2,3.4" 或 "100 200 300"）
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\d\.\-,\s]+$"))
                return @"^[\d\.\-,\s]+$";

            // 纯十六进制（如 "a1b2c3d4"，至少8位）
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[0-9a-fA-F]+$") && trimmed.Length >= 8)
                return @"^[0-9a-fA-F]{8,}$";

            // 包含多个连续的 UUID/GUID 分隔符
            if (trimmed.Count(c => c == '-') >= 4)
                return @"^[0-9a-fA-F-]+$";

            // 默认：转义特殊字符后的字面匹配（前10个字符）
            // 对于很长的文本，只匹配开头部分
            string sample = trimmed.Length > 20 ? trimmed.Substring(0, 20) : trimmed;
            return "^" + System.Text.RegularExpressions.Regex.Escape(sample) + ".*$";
        }

        /// <summary>
        /// 显示通知消息
        /// </summary>
        private async Task ShowNotificationAsync(string message, bool isSuccess)
        {
            if (_logService != null)
            {
                await _logService.LogAsync(message, isSuccess ? LogLevel.Info : LogLevel.Warning);
            }

            // 可选：使用 InfoBar 显示临时通知（需要在 XAML 中添加）
            // 这里先通过日志系统通知
        }
    }

    // ==================== 辅助类 ====================

    /// <summary>
    /// 字段名条目
    /// </summary>
    public class FieldNameEntry : INotifyPropertyChanged
    {
        private string _fieldName = "";

        public string FieldName
        {
            get => _fieldName;
            set
            {
                if (_fieldName != value)
                {
                    _fieldName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FieldName)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// 排除模式条目
    /// </summary>
    public class PatternEntry : INotifyPropertyChanged
    {
        private string _pattern = "";

        public string Pattern
        {
            get => _pattern;
            set
            {
                if (_pattern != value)
                {
                    _pattern = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pattern)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
