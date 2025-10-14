using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Services;
using Microsoft.UI.Dispatching;

namespace XUnity_LLMTranslatePlus.Views
{
    /// <summary>
    /// 术语库页面
    /// </summary>
    public sealed partial class TerminologyPage : Page
    {
        public ObservableCollection<Term> Terms { get; set; }

        private readonly ConfigService? _configService;
        private readonly TerminologyService? _terminologyService;
        private readonly LogService? _logService;
        private AppConfig? _currentConfig;
        private DispatcherQueueTimer? _autoSaveTimer;
        private bool _isLoadingConfig = false;
        private string _currentTerminologyFile = "default";

        public TerminologyPage()
        {
            this.InitializeComponent();
            Terms = new ObservableCollection<Term>();
            TermsListView.ItemsSource = Terms;

            // 获取服务
            _configService = App.GetService<ConfigService>();
            _terminologyService = App.GetService<TerminologyService>();
            _logService = App.GetService<LogService>();

            // 初始化自动保存定时器（防抖：800ms 后保存）
            _autoSaveTimer = DispatcherQueue.CreateTimer();
            _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(800);
            _autoSaveTimer.Tick += (s, e) =>
            {
                _autoSaveTimer.Stop();
                _ = AutoSaveTermsAsync();
            };

            // 页面卸载时立即保存
            this.Unloaded += async (s, e) =>
            {
                _autoSaveTimer?.Stop();
                await AutoSaveTermsAsync();
            };

            // 加载配置和术语
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

                // 加载可用的术语库文件列表
                LoadAvailableTerminologyFiles();

                if (_terminologyService != null)
                {
                    // 根据配置加载对应的术语库文件
                    string terminologyPath = GetTerminologyFilePath(_currentTerminologyFile);
                    await _terminologyService.LoadTermsAsync(terminologyPath);
                    var terms = _terminologyService.GetTerms();
                    Terms.Clear();
                    foreach (var term in terms)
                    {
                        Terms.Add(term);
                    }
                }
            }
            catch (Exception ex)
            {
                _logService?.Log($"加载术语库配置失败: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                _isLoadingConfig = false;
            }
        }

        private void ApplyConfigToUI(AppConfig config)
        {
            _currentTerminologyFile = string.IsNullOrEmpty(config.CurrentTerminologyFile)
                ? "default"
                : config.CurrentTerminologyFile;

            EnableSmartTerminologyToggle.IsOn = config.EnableSmartTerminology;
        }

        private void LoadAvailableTerminologyFiles()
        {
            try
            {
                string terminologiesFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "XUnity-LLMTranslatePlus",
                    "Terminologies"
                );

                // 确保文件夹存在
                if (!Directory.Exists(terminologiesFolder))
                {
                    Directory.CreateDirectory(terminologiesFolder);
                }

                // 获取所有 .csv 文件
                var csvFiles = Directory.GetFiles(terminologiesFolder, "*.csv")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .ToList();

                // 如果没有任何文件，创建默认文件
                if (csvFiles.Count == 0)
                {
                    csvFiles.Add("default");
                }

                // 填充 ComboBox
                TerminologyFileComboBox.Items.Clear();
                foreach (var file in csvFiles)
                {
                    TerminologyFileComboBox.Items.Add(file);
                }

                // 选中当前术语库
                var index = csvFiles.IndexOf(_currentTerminologyFile);
                TerminologyFileComboBox.SelectedIndex = index >= 0 ? index : 0;
            }
            catch (Exception ex)
            {
                _logService?.Log($"加载术语库文件列表失败: {ex.Message}", LogLevel.Error);
            }
        }

        private async void TerminologyFileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingConfig || TerminologyFileComboBox.SelectedItem == null) return;

            try
            {
                string selectedFile = TerminologyFileComboBox.SelectedItem.ToString() ?? "default";

                // 保存当前术语库
                await AutoSaveTermsAsync();

                // 切换到新的术语库
                _currentTerminologyFile = selectedFile;

                // 更新配置
                if (_currentConfig != null)
                {
                    _currentConfig.CurrentTerminologyFile = _currentTerminologyFile;
                    await _configService!.SaveConfigAsync(_currentConfig);
                }

                // 加载新的术语库
                string terminologyPath = GetTerminologyFilePath(selectedFile);
                if (_terminologyService != null)
                {
                    await _terminologyService.LoadTermsAsync(terminologyPath);
                    var terms = _terminologyService.GetTerms();
                    Terms.Clear();
                    foreach (var term in terms)
                    {
                        Terms.Add(term);
                    }
                }

                _logService?.Log($"已切换到术语库: {selectedFile}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _logService?.Log($"切换术语库失败: {ex.Message}", LogLevel.Error);
            }
        }

        private async void NewTerminologyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 创建输入对话框
                TextBox inputTextBox = new TextBox
                {
                    PlaceholderText = "输入新术语库名称",
                    Text = "new_terminology"
                };

                ContentDialog dialog = new ContentDialog
                {
                    Title = "创建新术语库",
                    Content = inputTextBox,
                    PrimaryButtonText = "创建",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    string newFileName = inputTextBox.Text.Trim();
                    if (string.IsNullOrEmpty(newFileName))
                    {
                        await ShowMessageDialog("错误", "术语库名称不能为空");
                        return;
                    }

                    // 检查文件是否已存在
                    string newFilePath = GetTerminologyFilePath(newFileName);
                    if (File.Exists(newFilePath))
                    {
                        await ShowMessageDialog("错误", "该术语库名称已存在");
                        return;
                    }

                    // 创建新的空术语库
                    _terminologyService?.ClearTerms();
                    await _terminologyService!.SaveTermsAsync(newFilePath);

                    // 刷新列表并选中新文件
                    LoadAvailableTerminologyFiles();
                    _currentTerminologyFile = newFileName;
                    TerminologyFileComboBox.SelectedItem = newFileName;

                    Terms.Clear();

                    _logService?.Log($"已创建新术语库: {newFileName}", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                _logService?.Log($"创建新术语库失败: {ex.Message}", LogLevel.Error);
                await ShowMessageDialog("错误", $"创建失败: {ex.Message}");
            }
        }

        private async void SaveAsTerminologyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 创建输入对话框
                TextBox inputTextBox = new TextBox
                {
                    PlaceholderText = "输入术语库名称",
                    Text = $"{_currentTerminologyFile}_copy"
                };

                ContentDialog dialog = new ContentDialog
                {
                    Title = "另存为术语库",
                    Content = inputTextBox,
                    PrimaryButtonText = "保存",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    string newFileName = inputTextBox.Text.Trim();
                    if (string.IsNullOrEmpty(newFileName))
                    {
                        await ShowMessageDialog("错误", "术语库名称不能为空");
                        return;
                    }

                    // 保存到新文件
                    string newFilePath = GetTerminologyFilePath(newFileName);

                    // 更新 TerminologyService 中的术语
                    _terminologyService?.ClearTerms();
                    foreach (var term in Terms)
                    {
                        _terminologyService?.AddTerm(term);
                    }

                    await _terminologyService!.SaveTermsAsync(newFilePath);

                    // 刷新列表并选中新文件
                    LoadAvailableTerminologyFiles();
                    _currentTerminologyFile = newFileName;
                    TerminologyFileComboBox.SelectedItem = newFileName;

                    _logService?.Log($"已另存为术语库: {newFileName}", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                _logService?.Log($"另存为术语库失败: {ex.Message}", LogLevel.Error);
                await ShowMessageDialog("错误", $"保存失败: {ex.Message}");
            }
        }

        private async void DeleteTerminologyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTerminologyFile == "default")
            {
                await ShowMessageDialog("提示", "不能删除默认术语库");
                return;
            }

            try
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要删除术语库 '{_currentTerminologyFile}' 吗？此操作不可恢复。",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    string filePath = GetTerminologyFilePath(_currentTerminologyFile);
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    // 切换到默认术语库
                    _currentTerminologyFile = "default";
                    LoadAvailableTerminologyFiles();

                    // 重新加载术语
                    if (_terminologyService != null)
                    {
                        string terminologyPath = GetTerminologyFilePath(_currentTerminologyFile);
                        await _terminologyService.LoadTermsAsync(terminologyPath);
                        var terms = _terminologyService.GetTerms();
                        Terms.Clear();
                        foreach (var term in terms)
                        {
                            Terms.Add(term);
                        }
                    }

                    _logService?.Log($"已删除术语库", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                _logService?.Log($"删除术语库失败: {ex.Message}", LogLevel.Error);
                await ShowMessageDialog("错误", $"删除失败: {ex.Message}");
            }
        }

        private void AddTermButton_Click(object sender, RoutedEventArgs e)
        {
            var newTerm = new Term { Original = "", Translation = "", Enabled = true };
            Terms.Add(newTerm);

            // 自动滚动到新添加的项
            TermsListView.SelectedItem = newTerm;
            TermsListView.ScrollIntoView(newTerm);

            TriggerAutoSave();
        }

        private void DeleteTermButton_Click(object sender, RoutedEventArgs e)
        {
            if (TermsListView.SelectedItem is Term selectedTerm)
            {
                Terms.Remove(selectedTerm);
                _terminologyService?.RemoveTerm(selectedTerm);
                TriggerAutoSave();
            }
        }

        private async void ImportTermsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_terminologyService == null)
            {
                await ShowMessageDialog("错误", "术语库服务未初始化");
                return;
            }

            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".csv");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    int count = await _terminologyService.ImportCsvAsync(file.Path);

                    // 刷新 UI
                    var terms = _terminologyService.GetTerms();
                    Terms.Clear();
                    foreach (var term in terms)
                    {
                        Terms.Add(term);
                    }

                    await ShowMessageDialog("导入成功", $"成功导入 {count} 条术语");
                    TriggerAutoSave();
                }
            }
            catch (Exception ex)
            {
                await ShowMessageDialog("导入失败", $"导入失败: {ex.Message}");
            }
        }

        private async void ExportTermsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_terminologyService == null)
            {
                await ShowMessageDialog("错误", "术语库服务未初始化");
                return;
            }

            try
            {
                var picker = new FileSavePicker();
                picker.FileTypeChoices.Add("CSV 文件", new[] { ".csv" });
                picker.SuggestedFileName = _currentTerminologyFile;

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    // 先更新 TerminologyService
                    _terminologyService.ClearTerms();
                    foreach (var term in Terms)
                    {
                        _terminologyService.AddTerm(term);
                    }

                    await _terminologyService.ExportCsvAsync(file.Path);
                    await ShowMessageDialog("导出成功", "术语库导出成功");
                }
            }
            catch (Exception ex)
            {
                await ShowMessageDialog("导出失败", $"导出失败: {ex.Message}");
            }
        }

        private async void EnableSmartTerminologyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig || _configService == null) return;

            try
            {
                // 加载最新配置以保留其他页面的设置
                var config = await _configService.LoadConfigAsync();
                config.EnableSmartTerminology = EnableSmartTerminologyToggle.IsOn;
                await _configService.SaveConfigAsync(config);

                // 更新当前配置字段，防止被其他操作覆盖
                _currentConfig = config;

                _logService?.Log($"智能术语提取已{(config.EnableSmartTerminology ? "启用" : "禁用")}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _logService?.Log($"保存智能术语提取配置失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 触发自动保存（带防抖）
        /// </summary>
        private void TriggerAutoSave()
        {
            if (_isLoadingConfig || _terminologyService == null) return;

            // 重启定时器（防抖）
            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Start();
        }

        /// <summary>
        /// 术语 TextBox 文本变化时触发自动保存
        /// </summary>
        private void TermTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingConfig) return;
            TriggerAutoSave();
        }

        /// <summary>
        /// 术语 CheckBox 状态变化时触发自动保存
        /// </summary>
        private void TermCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingConfig) return;
            TriggerAutoSave();
        }

        /// <summary>
        /// 自动保存术语库
        /// </summary>
        private async System.Threading.Tasks.Task AutoSaveTermsAsync()
        {
            if (_terminologyService == null) return;

            try
            {
                // 保存术语库 - 先清空再添加
                _terminologyService.ClearTerms();
                foreach (var term in Terms)
                {
                    _terminologyService.AddTerm(term);
                }

                string filePath = GetTerminologyFilePath(_currentTerminologyFile);
                await _terminologyService.SaveTermsAsync(filePath);

                _logService?.Log("术语库已自动保存", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logService?.Log($"自动保存术语库失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 获取术语库文件的完整路径
        /// </summary>
        private string GetTerminologyFilePath(string fileName)
        {
            string terminologiesFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XUnity-LLMTranslatePlus",
                "Terminologies"
            );

            return Path.Combine(terminologiesFolder, $"{fileName}.csv");
        }

        /// <summary>
        /// 显示消息对话框
        /// </summary>
        private async System.Threading.Tasks.Task ShowMessageDialog(string title, string message)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
