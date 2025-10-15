using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Services;

namespace XUnity_LLMTranslatePlus.Views
{
    /// <summary>
    /// API配置页面（重新设计为支持多API负载均衡）
    /// </summary>
    public sealed partial class ApiConfigPage : Page
    {
        private readonly ConfigService? _configService;
        private readonly ApiClient? _apiClient;
        private readonly LogService? _logService;
        private readonly ApiPoolManager? _poolManager;
        private AppConfig? _currentConfig;
        private DispatcherQueueTimer? _autoSaveTimer;
        private DispatcherQueueTimer? _statsRefreshTimer;
        private bool _isLoadingConfig = false;

        private ObservableCollection<ApiEndpoint> _endpoints = new ObservableCollection<ApiEndpoint>();

        public ApiConfigPage()
        {
            this.InitializeComponent();

            // 获取服务
            _configService = App.GetService<ConfigService>();
            _apiClient = App.GetService<ApiClient>();
            _logService = App.GetService<LogService>();
            _poolManager = App.GetService<ApiPoolManager>();

            // 初始化自动保存定时器
            _autoSaveTimer = DispatcherQueue.CreateTimer();
            _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(800);
            _autoSaveTimer.Tick += (s, e) =>
            {
                _autoSaveTimer.Stop();
                _ = AutoSaveConfigAsync();
            };

            // 初始化统计刷新定时器（每5秒刷新一次）
            _statsRefreshTimer = DispatcherQueue.CreateTimer();
            _statsRefreshTimer.Interval = TimeSpan.FromSeconds(5);
            _statsRefreshTimer.Tick += (s, e) => RefreshStatistics();

            // 加载配置
            LoadConfiguration();

            // 启动统计刷新
            _statsRefreshTimer.Start();

            // 订阅池管理器统计更新事件
            if (_poolManager != null)
            {
                _poolManager.StatsUpdated += OnStatsUpdated;
            }
        }

        /// <summary>
        /// 页面加载事件（修复切换页面后统计信息丢失的问题）
        /// </summary>
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 延迟 200ms 等待 ListView 渲染完成
            await Task.Delay(200);

            // 刷新统计信息
            RefreshStatistics();

            _logService?.Log("API配置页面已加载，统计信息已刷新", LogLevel.Debug);
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
            }
            finally
            {
                _isLoadingConfig = false;
            }
        }

        private void ApplyConfigToUI(AppConfig config)
        {
            // 加载端点列表
            _endpoints.Clear();
            if (config.ApiEndpoints != null)
            {
                foreach (var endpoint in config.ApiEndpoints)
                {
                    _endpoints.Add(endpoint);
                }
            }
            EndpointsListView.ItemsSource = _endpoints;
            UpdateEndpointCount();
            UpdateEmptyState();

            // 刷新统计信息
            RefreshStatistics();
        }

        private void UpdateEndpointCount()
        {
            int enabledCount = _endpoints.Count(e => e.IsEnabled);
            EndpointCountText.Text = $"共 {_endpoints.Count} 个端点（{enabledCount} 个已启用）";
        }

        private void UpdateEmptyState()
        {
            if (_endpoints.Count == 0)
            {
                EmptyStateBorder.Visibility = Visibility.Visible;
                EndpointsListView.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyStateBorder.Visibility = Visibility.Collapsed;
                EndpointsListView.Visibility = Visibility.Visible;
            }
        }

        private void RefreshStatistics()
        {
            if (_poolManager == null) return;

            try
            {
                var allStats = _poolManager.GetAllStats();

                // 更新每个端点的统计显示
                foreach (var item in EndpointsListView.Items)
                {
                    if (item is ApiEndpoint endpoint)
                    {
                        var stats = allStats.FirstOrDefault(s => s.EndpointId == endpoint.Id);
                        UpdateEndpointStatsDisplay(endpoint.Id, stats);
                    }
                }
            }
            catch (Exception ex)
            {
                _logService?.Log($"刷新统计失败: {ex.Message}", LogLevel.Warning);
            }
        }

        private void UpdateEndpointStatsDisplay(string endpointId, ApiEndpointStats? stats)
        {
            try
            {
                // 在ListView中查找对应的统计文本控件
                for (int i = 0; i < EndpointsListView.Items.Count; i++)
                {
                    var container = EndpointsListView.ContainerFromIndex(i) as ListViewItem;
                    if (container == null)
                    {
                        // ListView item未渲染，跳过
                        continue;
                    }

                    var statsText = FindChildByName(container, "StatsText") as TextBlock;
                    if (statsText != null && statsText.Tag?.ToString() == endpointId)
                    {
                        if (stats != null && stats.TotalRequests > 0)
                        {
                            statsText.Text = $"✓ 成功率: {stats.SuccessRate:P0} | 平均: {stats.AverageResponseTime:F0}ms | 请求: {stats.TotalRequests} | 活跃: {stats.ActiveRequests}";
                            statsText.Foreground = stats.SuccessRate > 0.8 ?
                                new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green) :
                                new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
                        }
                        else
                        {
                            // 显示为初始状态（浅灰色，表示未开始使用）
                            statsText.Text = "未使用（点击测试按钮可测试连接）";
                            statsText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
                        }
                        break; // 找到了就退出循环
                    }
                }
            }
            catch (Exception ex)
            {
                _logService?.Log($"更新端点统计显示失败 ({endpointId}): {ex.Message}", LogLevel.Debug);
            }
        }

        private DependencyObject? FindChildByName(DependencyObject parent, string name)
        {
            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && element.Name == name)
                    return child;

                var result = FindChildByName(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void OnStatsUpdated(object? sender, ApiEndpointStats stats)
        {
            // 在UI线程更新统计显示
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateEndpointStatsDisplay(stats.EndpointId, stats);
            });
        }

        private async void AddEndpointButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ApiEndpointEditDialog
            {
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.Endpoint != null)
            {
                _endpoints.Add(dialog.Endpoint);
                UpdateEndpointCount();
                UpdateEmptyState();
                TriggerAutoSave();

                await _logService?.LogAsync($"添加API端点: {dialog.Endpoint.Name}", LogLevel.Info)!;

                // 为新端点初始化统计信息（延迟100ms等待UI渲染完成）
                await Task.Delay(100);
                RefreshStatistics();
            }
        }

        private async void EditEndpointButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string endpointId)
            {
                var endpoint = _endpoints.FirstOrDefault(ep => ep.Id == endpointId);
                if (endpoint == null) return;

                var dialog = new ApiEndpointEditDialog
                {
                    XamlRoot = this.XamlRoot
                };
                dialog.LoadEndpoint(endpoint);

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary && dialog.Endpoint != null)
                {
                    // 更新端点
                    var index = _endpoints.IndexOf(endpoint);
                    if (index >= 0)
                    {
                        _endpoints[index] = dialog.Endpoint;
                        // 刷新ListView
                        EndpointsListView.ItemsSource = null;
                        EndpointsListView.ItemsSource = _endpoints;
                        TriggerAutoSave();

                        await _logService?.LogAsync($"编辑API端点: {dialog.Endpoint.Name}", LogLevel.Info)!;
                    }
                }
            }
        }

        private async void DeleteEndpointButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string endpointId)
            {
                var endpoint = _endpoints.FirstOrDefault(ep => ep.Id == endpointId);
                if (endpoint == null) return;

                // 确认删除
                var confirmDialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要删除端点【{endpoint.Name}】吗？此操作无法撤销。",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var result = await confirmDialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    _endpoints.Remove(endpoint);
                    UpdateEndpointCount();
                    UpdateEmptyState();
                    TriggerAutoSave();

                    await _logService?.LogAsync($"删除API端点: {endpoint.Name}", LogLevel.Info)!;
                }
            }
        }

        private async void TestEndpointButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string endpointId)
            {
                var endpoint = _endpoints.FirstOrDefault(ep => ep.Id == endpointId);
                if (endpoint == null) return;

                button.IsEnabled = false;
                button.Content = "测试中...";

                try
                {
                    await _logService?.LogAsync($"开始测试端点: {endpoint.Name}", LogLevel.Info)!;

                    string testText = "Hello";
                    string testPrompt = "Translate to Chinese: ";

                    string result = await _apiClient!.TranslateAsync(testText, testPrompt, endpoint);

                    var successDialog = new ContentDialog
                    {
                        Title = "测试成功",
                        Content = $"端点【{endpoint.Name}】连接成功！\n\n测试翻译结果: {result}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();

                    await _logService?.LogAsync($"端点测试成功: {endpoint.Name}", LogLevel.Info)!;
                }
                catch (Exception ex)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "测试失败",
                        Content = $"端点【{endpoint.Name}】测试失败:\n\n{ex.Message}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();

                    await _logService?.LogAsync($"端点测试失败: {endpoint.Name} - {ex.Message}", LogLevel.Error)!;
                }
                finally
                {
                    button.IsEnabled = true;
                    button.Content = "测试";
                }
            }
        }

        private async void TestAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_endpoints.Count == 0)
            {
                var dialog = new ContentDialog
                {
                    Title = "提示",
                    Content = "没有可测试的端点",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            var button = sender as Button;
            if (button != null)
            {
                button.IsEnabled = false;
                button.Content = "测试中...";
            }

            int successCount = 0;
            int failureCount = 0;

            foreach (var endpoint in _endpoints.Where(e => e.IsEnabled))
            {
                try
                {
                    string testText = "Hello";
                    string testPrompt = "Translate to Chinese: ";
                    await _apiClient!.TranslateAsync(testText, testPrompt, endpoint);
                    successCount++;
                }
                catch
                {
                    failureCount++;
                }
            }

            if (button != null)
            {
                button.IsEnabled = true;
                button.Content = "全部测试";
            }

            var resultDialog = new ContentDialog
            {
                Title = "测试完成",
                Content = $"测试结果:\n\n成功: {successCount}\n失败: {failureCount}\n总计: {successCount + failureCount}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await resultDialog.ShowAsync();
        }

        private void RefreshStatsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshStatistics();
        }

        private void EndpointToggle_Toggled(object sender, RoutedEventArgs e)
        {
            TriggerAutoSave();
            UpdateEndpointCount();
        }

        private void TriggerAutoSave()
        {
            if (_isLoadingConfig || _configService == null) return;

            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Start();
        }

        private async Task AutoSaveConfigAsync()
        {
            if (_configService == null || _currentConfig == null) return;

            try
            {
                // 从UI获取配置
                var config = await GetConfigFromUIAsync();
                await _configService.SaveConfigAsync(config);

                // 更新本地缓存
                _currentConfig = config;

                // 更新API池端点配置（保留统计数据）
                if (_poolManager != null)
                {
                    await _poolManager.UpdateEndpointsAsync();
                }

                _logService?.Log("API配置已自动保存", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logService?.Log($"自动保存API配置失败: {ex.Message}", LogLevel.Error);
            }
        }

        private async Task<AppConfig> GetConfigFromUIAsync()
        {
            // 始终从ConfigService获取最新配置，确保不丢失其他页面的设置
            var config = await _configService!.LoadConfigAsync();

            // 更新多API端点配置
            config.ApiEndpoints = _endpoints.ToList();

            return config;
        }
    }
}
