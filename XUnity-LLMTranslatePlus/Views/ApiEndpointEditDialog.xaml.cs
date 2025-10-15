using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XUnity_LLMTranslatePlus.Models;

namespace XUnity_LLMTranslatePlus.Views
{
    /// <summary>
    /// API端点编辑对话框
    /// </summary>
    public sealed partial class ApiEndpointEditDialog : ContentDialog
    {
        public ApiEndpoint? Endpoint { get; private set; }
        private bool _isEditMode = false;

        public ApiEndpointEditDialog()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// 加载端点数据（用于编辑）
        /// </summary>
        public void LoadEndpoint(ApiEndpoint endpoint)
        {
            if (endpoint == null) return;

            _isEditMode = true;
            Endpoint = endpoint.Clone(); // 克隆以避免直接修改

            // 填充表单
            NameTextBox.Text = endpoint.Name;
            UrlTextBox.Text = endpoint.Url;
            ApiKeyPasswordBox.Password = endpoint.ApiKey;
            ModelNameTextBox.Text = endpoint.ModelName;
            MaxTokensNumberBox.Value = endpoint.MaxTokens;
            TemperatureSlider.Value = endpoint.Temperature;
            TimeoutNumberBox.Value = endpoint.Timeout;
            MaxConcurrentNumberBox.Value = endpoint.MaxConcurrent;
            WeightNumberBox.Value = endpoint.Weight;
            IsEnabledToggle.IsOn = endpoint.IsEnabled;

            // 设置平台（尝试匹配）
            SetPlatformFromEndpoint(endpoint);
        }

        /// <summary>
        /// 从端点数据设置平台选择
        /// </summary>
        private void SetPlatformFromEndpoint(ApiEndpoint endpoint)
        {
            // 尝试根据平台名称匹配ComboBox项
            for (int i = 0; i < PlatformComboBox.Items.Count; i++)
            {
                if (PlatformComboBox.Items[i] is ComboBoxItem item)
                {
                    if (item.Content.ToString() == endpoint.Platform)
                    {
                        PlatformComboBox.SelectedIndex = i;
                        return;
                    }
                }
            }

            // 如果没有匹配，选择"自定义"
            PlatformComboBox.SelectedIndex = PlatformComboBox.Items.Count - 1;
        }

        private void PlatformComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlatformComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                // 解析预设值：Platform|ApiFormat|Url|ModelName
                var parts = tag.Split('|');
                if (parts.Length >= 4)
                {
                    // 只在非编辑模式或值为空时应用预设
                    if (string.IsNullOrWhiteSpace(UrlTextBox.Text) || !_isEditMode)
                    {
                        UrlTextBox.Text = parts[2];
                    }

                    if (string.IsNullOrWhiteSpace(ModelNameTextBox.Text) || !_isEditMode)
                    {
                        ModelNameTextBox.Text = parts[3];
                    }
                }
            }
        }

        private void TemperatureSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (TemperatureValueText != null)
            {
                TemperatureValueText.Text = e.NewValue.ToString("F1");
            }
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // 验证表单
            if (!ValidateForm())
            {
                args.Cancel = true;
                return;
            }

            // 创建或更新端点对象
            if (Endpoint == null)
            {
                Endpoint = new ApiEndpoint
                {
                    Id = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.Now
                };
            }

            // 从表单获取值
            Endpoint.Name = NameTextBox.Text.Trim();
            Endpoint.Url = UrlTextBox.Text.Trim();
            Endpoint.ApiKey = ApiKeyPasswordBox.Password;
            Endpoint.ModelName = ModelNameTextBox.Text.Trim();
            Endpoint.MaxTokens = (int)MaxTokensNumberBox.Value;
            Endpoint.Temperature = TemperatureSlider.Value;
            Endpoint.TopP = 1.0; // 使用默认值
            Endpoint.FrequencyPenalty = 0.0;
            Endpoint.PresencePenalty = 0.0;
            Endpoint.Timeout = (int)TimeoutNumberBox.Value;
            Endpoint.MaxConcurrent = (int)MaxConcurrentNumberBox.Value;
            Endpoint.Weight = (int)WeightNumberBox.Value;
            Endpoint.IsEnabled = IsEnabledToggle.IsOn;

            // 设置平台和格式
            if (PlatformComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                var parts = tag.Split('|');
                if (parts.Length >= 2)
                {
                    Endpoint.Platform = parts[0];
                    Endpoint.ApiFormat = parts[1];
                }
            }
        }

        private void ContentDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            Endpoint = null; // 取消时清空
        }

        /// <summary>
        /// 验证表单输入
        /// </summary>
        private bool ValidateForm()
        {
            // 验证名称
            if (string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                ShowErrorInline("请输入端点名称");
                return false;
            }

            // 验证URL
            if (string.IsNullOrWhiteSpace(UrlTextBox.Text))
            {
                ShowErrorInline("请输入API地址");
                return false;
            }

            if (!Uri.TryCreate(UrlTextBox.Text.Trim(), UriKind.Absolute, out _))
            {
                ShowErrorInline("API地址格式无效");
                return false;
            }

            // 验证API密钥
            if (string.IsNullOrWhiteSpace(ApiKeyPasswordBox.Password))
            {
                ShowErrorInline("请输入API密钥");
                return false;
            }

            // 验证模型名称
            if (string.IsNullOrWhiteSpace(ModelNameTextBox.Text))
            {
                ShowErrorInline("请输入模型名称");
                return false;
            }

            // 验证通过，关闭InfoBar
            ErrorInfoBar.IsOpen = false;
            return true;
        }

        /// <summary>
        /// 显示错误消息（使用内联InfoBar）
        /// </summary>
        private void ShowErrorInline(string message)
        {
            ErrorInfoBar.Message = message;
            ErrorInfoBar.IsOpen = true;
        }

        /// <summary>
        /// 获取模型列表按钮点击事件
        /// </summary>
        private async void FetchModelsButton_Click(object sender, RoutedEventArgs e)
        {
            // 验证必填字段
            if (string.IsNullOrWhiteSpace(UrlTextBox.Text))
            {
                ShowErrorInline("请先输入API地址");
                return;
            }

            if (string.IsNullOrWhiteSpace(ApiKeyPasswordBox.Password))
            {
                ShowErrorInline("请先输入API密钥");
                return;
            }

            // 关闭错误提示
            ErrorInfoBar.IsOpen = false;

            // 禁用按钮，防止重复点击
            FetchModelsButton.IsEnabled = false;
            FetchModelsButton.Content = "获取中...";

            try
            {
                // 获取ApiClient服务
                var apiClient = App.GetService<Services.ApiClient>();
                if (apiClient == null)
                {
                    ShowErrorInline("无法获取ApiClient服务");
                    return;
                }

                // 创建临时端点对象
                var tempEndpoint = new ApiEndpoint
                {
                    Url = UrlTextBox.Text.Trim(),
                    ApiKey = ApiKeyPasswordBox.Password
                };

                // 获取模型列表
                var models = await apiClient.GetModelsAsync(tempEndpoint);

                if (models == null || models.Count == 0)
                {
                    ShowErrorInline("未获取到模型列表，请检查API地址和密钥是否正确");
                    return;
                }

                // 创建模型选择ListView
                var listView = new ListView
                {
                    ItemsSource = models,
                    SelectionMode = ListViewSelectionMode.Single,
                    MaxHeight = 400
                };

                // 如果当前模型名称在列表中，自动选中
                if (!string.IsNullOrWhiteSpace(ModelNameTextBox.Text))
                {
                    var currentModel = models.FirstOrDefault(m => m == ModelNameTextBox.Text);
                    if (currentModel != null)
                    {
                        listView.SelectedItem = currentModel;
                    }
                }

                // 显示选择对话框（使用Flyout避免ContentDialog嵌套问题）
                var flyout = new Flyout
                {
                    Content = new StackPanel
                    {
                        Width = 300,
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"找到 {models.Count} 个模型",
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                            },
                            listView,
                            new Button
                            {
                                Content = "确定",
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                Style = (Style)Application.Current.Resources["AccentButtonStyle"]
                            }
                        }
                    }
                };

                // 确定按钮点击事件
                var confirmButton = ((StackPanel)flyout.Content).Children.OfType<Button>().First();
                confirmButton.Click += (s, args) =>
                {
                    if (listView.SelectedItem is string selectedModel)
                    {
                        ModelNameTextBox.Text = selectedModel;
                    }
                    flyout.Hide();
                };

                // 显示Flyout
                flyout.ShowAt(FetchModelsButton);
            }
            catch (Exception ex)
            {
                ShowErrorInline($"获取模型列表失败: {ex.Message}");
            }
            finally
            {
                // 恢复按钮状态
                FetchModelsButton.IsEnabled = true;
                FetchModelsButton.Content = "获取模型";
            }
        }
    }
}
