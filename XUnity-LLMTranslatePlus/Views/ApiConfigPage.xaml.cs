using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Services;
using Microsoft.UI.Dispatching;

namespace XUnity_LLMTranslatePlus.Views
{
    /// <summary>
    /// API 配置页面
    /// </summary>
    public sealed partial class ApiConfigPage : Page
    {
        private readonly ConfigService? _configService;
        private readonly ApiClient? _apiClient;
        private readonly LogService? _logService;
        private AppConfig? _currentConfig;
        private DispatcherQueueTimer? _autoSaveTimer;
        private bool _isLoadingConfig = false;

        // API平台配置映射
        private readonly Dictionary<string, (string format, string defaultUrl)> _platformConfigs = new()
        {
            { "OpenAI", ("OpenAI", "https://api.openai.com/v1/chat/completions") },
            { "Azure OpenAI", ("OpenAI", "https://<your-resource>.openai.azure.com/openai/deployments/<deployment-id>/chat/completions?api-version=2024-02-15-preview") },
            { "Anthropic Claude", ("Claude", "https://api.anthropic.com/v1/messages") },
            { "Google Gemini", ("Gemini", "https://generativelanguage.googleapis.com/v1beta/models/<model>:generateContent") },
            { "DeepSeek", ("OpenAI", "https://api.deepseek.com/chat/completions") },
            { "Moonshot", ("OpenAI", "https://api.moonshot.cn/v1/chat/completions") },
            { "智谱GLM", ("OpenAI", "https://open.bigmodel.cn/api/paas/v4/chat/completions") },
            { "Ollama", ("OpenAI", "http://localhost:11434/v1/chat/completions") },
            { "自定义", ("OpenAI", "") }
        };

        public ApiConfigPage()
        {
            this.InitializeComponent();

            // 获取服务
            _configService = App.GetService<ConfigService>();
            _apiClient = App.GetService<ApiClient>();
            _logService = App.GetService<LogService>();

            // 初始化自动保存定时器（防抖：800ms 后保存，API配置相对复杂，延迟稍长）
            _autoSaveTimer = DispatcherQueue.CreateTimer();
            _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(800);
            _autoSaveTimer.Tick += (s, e) =>
            {
                _autoSaveTimer.Stop();
                _ = AutoSaveConfigAsync();
            };

            // 加载配置
            LoadConfiguration();

            // 添加Loaded事件确保模型名称正确显示
            this.Loaded += ApiConfigPage_Loaded;
        }

        private void ApiConfigPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 重新应用模型名称（确保控件已初始化）
            // 使用 DispatcherQueue 延迟设置，确保 ComboBox 完全渲染后再设置文本
            if (_currentConfig != null && ModelNameComboBox != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ModelNameComboBox.Text = _currentConfig.ModelName;
                });
            }
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
                _logService?.Log($"加载配置失败: {ex.Message}", LogLevel.Error);
                // 加载配置失败时静默处理，不显示错误对话框
            }
            finally
            {
                _isLoadingConfig = false;
            }
        }

        private void ApplyConfigToUI(AppConfig config)
        {
            // 基础配置
            // 根据平台名称设置选择索引
            int platformIndex = config.ApiPlatform switch
            {
                "OpenAI" => 0,
                "Azure OpenAI" => 1,
                "Anthropic Claude" => 2,
                "Google Gemini" => 3,
                "DeepSeek" => 4,
                "Moonshot" => 5,
                "智谱GLM" => 6,
                "Ollama" => 7,
                _ => 8 // 自定义
            };
            ApiPlatformComboBox.SelectedIndex = platformIndex;
            ApiUrlTextBox.Text = config.ApiUrl;
            ApiKeyPasswordBox.Password = config.ApiKey;
            
            // Model Name 是 ComboBox，先设置为可编辑模式再设置文本
            // 确保ComboBox是可编辑的，这样即使不在Items中也能显示
            if (ModelNameComboBox != null)
            {
                ModelNameComboBox.IsEditable = true;
                ModelNameComboBox.Text = config.ModelName;
            }

            // 高级配置
            MaxTokensNumberBox.Value = config.MaxTokens;
            TemperatureSlider.Value = config.Temperature;
            TopPSlider.Value = config.TopP;
            FrequencyPenaltySlider.Value = config.FrequencyPenalty;
            PresencePenaltySlider.Value = config.PresencePenalty;
            TimeoutNumberBox.Value = config.Timeout;
            RetryCountNumberBox.Value = config.RetryCount;

            // 并发配置
            MaxConcurrentTranslationsNumberBox.Value = config.MaxConcurrentTranslations;
        }

        private void ApiPlatformComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ApiPlatformComboBox.SelectedItem is ComboBoxItem item)
            {
                string platformName = item.Content.ToString() ?? "自定义";

                // 如果找到平台配置，自动设置URL
                if (_platformConfigs.TryGetValue(platformName, out var config))
                {
                    if (ApiUrlTextBox != null && !string.IsNullOrEmpty(config.defaultUrl))
                    {
                        ApiUrlTextBox.Text = config.defaultUrl;
                    }
                }

                // 锁定API地址（只有自定义平台可以编辑）
                if (ApiUrlTextBox != null)
                {
                    ApiUrlTextBox.IsReadOnly = platformName != "自定义";
                }
            }
            TriggerAutoSave();
        }

        private async System.Threading.Tasks.Task<AppConfig> GetConfigFromUIAsync()
        {
            // 始终从 ConfigService 获取最新配置，确保不丢失其他页面的设置
            var config = await _configService!.LoadConfigAsync();

            // 基础配置 - 获取平台名称
            string platformName = (ApiPlatformComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "自定义";
            config.ApiPlatform = platformName;

            // 设置API格式
            if (_platformConfigs.TryGetValue(platformName, out var platformConfig))
            {
                config.ApiFormat = platformConfig.format;
            }
            else
            {
                config.ApiFormat = "OpenAI"; // 默认使用OpenAI格式
            }

            // 自动补全API地址
            config.ApiUrl = AutoCompleteApiUrl(ApiUrlTextBox.Text, platformName);
            config.ApiKey = ApiKeyPasswordBox.Password;
            config.ModelName = ModelNameComboBox.Text;

            // 高级配置
            config.MaxTokens = (int)MaxTokensNumberBox.Value;
            config.Temperature = TemperatureSlider.Value;
            config.TopP = TopPSlider.Value;
            config.FrequencyPenalty = FrequencyPenaltySlider.Value;
            config.PresencePenalty = PresencePenaltySlider.Value;
            config.Timeout = (int)TimeoutNumberBox.Value;
            config.RetryCount = (int)RetryCountNumberBox.Value;

            // 并发配置
            config.MaxConcurrentTranslations = (int)MaxConcurrentTranslationsNumberBox.Value;

            return config;
        }

        private string AutoCompleteApiUrl(string url, string platformName)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            // 自动补全规则
            var completionRules = new Dictionary<string, (string pattern, string completion)>
            {
                { "DeepSeek", ("api.deepseek.com", "https://api.deepseek.com/chat/completions") },
                { "Moonshot", ("api.moonshot.cn", "https://api.moonshot.cn/v1/chat/completions") },
                { "智谱GLM", ("open.bigmodel.cn", "https://open.bigmodel.cn/api/paas/v4/chat/completions") },
                { "OpenAI", ("api.openai.com", "https://api.openai.com/v1/chat/completions") },
                { "Ollama", ("localhost:11434", "http://localhost:11434/v1/chat/completions") }
            };

            // 检查是否需要补全
            if (completionRules.TryGetValue(platformName, out var rule))
            {
                // 如果URL包含关键域名但不完整，进行补全
                if (url.Contains(rule.pattern) && !url.EndsWith("/chat/completions") && !url.EndsWith("/completions"))
                {
                    return rule.completion;
                }
            }

            return url;
        }

        private void TemperatureSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (TemperatureValueText != null)
            {
                TemperatureValueText.Text = e.NewValue.ToString("F1");
            }
            TriggerAutoSave();
        }

        private void TopPSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (TopPValueText != null)
            {
                TopPValueText.Text = e.NewValue.ToString("F1");
            }
            TriggerAutoSave();
        }

        private void FrequencyPenaltySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (FrequencyPenaltyValueText != null)
            {
                FrequencyPenaltyValueText.Text = e.NewValue.ToString("F1");
            }
            TriggerAutoSave();
        }

        private void PresencePenaltySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (PresencePenaltyValueText != null)
            {
                PresencePenaltyValueText.Text = e.NewValue.ToString("F1");
            }
            TriggerAutoSave();
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null)
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "错误",
                    Content = "API 客户端未初始化",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            try
            {
                var config = await GetConfigFromUIAsync();

                // 显示进度对话框
                ContentDialog progressDialog = new ContentDialog
                {
                    Title = "测试连接",
                    Content = new ProgressRing { IsActive = true, Width = 50, Height = 50 },
                    XamlRoot = this.XamlRoot
                };

                var dialogTask = progressDialog.ShowAsync();

                bool success = await _apiClient.TestConnectionAsync(config);

                progressDialog.Hide();

                ContentDialog resultDialog = new ContentDialog
                {
                    Title = success ? "连接成功" : "连接失败",
                    Content = success ? "API 连接测试成功！" : "API 连接测试失败，请检查配置。",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await resultDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                ContentDialog errorDialog = new ContentDialog
                {
                    Title = "测试连接失败",
                    Content = $"测试连接失败: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private async void RefreshModelsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null)
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "错误",
                    Content = "API 客户端未初始化",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            try
            {
                // 获取当前配置
                var config = await GetConfigFromUIAsync();

                // 显示进度
                RefreshModelsButton.IsEnabled = false;
                RefreshModelsButton.Content = "获取中...";

                // 获取模型列表
                var models = await _apiClient.GetModelsAsync(config);

                // 更新ComboBox
                if (models.Count > 0)
                {
                    string currentModel = ModelNameComboBox.Text;
                    ModelNameComboBox.Items.Clear();

                    foreach (var model in models)
                    {
                        ModelNameComboBox.Items.Add(new ComboBoxItem { Content = model });
                    }

                    // 恢复之前选择的模型
                    ModelNameComboBox.Text = currentModel;

                    ContentDialog successDialog = new ContentDialog
                    {
                        Title = "获取成功",
                        Content = $"成功获取 {models.Count} 个可用模型",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
                else
                {
                    ContentDialog noModelsDialog = new ContentDialog
                    {
                        Title = "未获取到模型",
                        Content = "未能获取到模型列表，请检查API配置和网络连接",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await noModelsDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                ContentDialog errorDialog = new ContentDialog
                {
                    Title = "获取失败",
                    Content = $"获取模型列表失败: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
            finally
            {
                RefreshModelsButton.IsEnabled = true;
                RefreshModelsButton.Content = "刷新列表";
            }
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
                // 从 UI 获取配置（内部会加载完整配置并只更新 API 相关字段）
                var config = await GetConfigFromUIAsync();
                await _configService.SaveConfigAsync(config);

                // 更新本地缓存
                _currentConfig = config;

                _logService?.Log("API 配置已自动保存", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logService?.Log($"自动保存 API 配置失败: {ex.Message}", LogLevel.Error);
            }
        }

        // 控件事件处理函数
        private void ApiUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TriggerAutoSave();
        }

        private void ApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            TriggerAutoSave();
        }

        private void ModelNameComboBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
        {
            TriggerAutoSave();
        }

        private void MaxTokensNumberBox_ValueChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
        {
            TriggerAutoSave();
        }

        private void TimeoutNumberBox_ValueChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
        {
            TriggerAutoSave();
        }

        private void RetryCountNumberBox_ValueChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
        {
            TriggerAutoSave();
        }

        private void MaxConcurrentTranslationsNumberBox_ValueChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
        {
            TriggerAutoSave();
        }
    }
}
