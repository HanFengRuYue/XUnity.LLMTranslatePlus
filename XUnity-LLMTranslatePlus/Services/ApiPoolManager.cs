using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Models;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// API 端点池管理器
    /// 负责管理多个API端点，动态分配任务，追踪性能指标
    /// </summary>
    public class ApiPoolManager
    {
        private readonly LogService _logService;
        private readonly ConfigService _configService;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        // 端点统计信息字典（线程安全）
        private readonly Dictionary<string, ApiEndpointStats> _statsMap = new Dictionary<string, ApiEndpointStats>();

        // 端点信号量字典（用于控制每个API的并发数）
        private readonly Dictionary<string, SemaphoreSlim> _endpointSemaphores = new Dictionary<string, SemaphoreSlim>();

        // 初始化状态标志（防止重复初始化）
        private bool _isInitialized = false;

        // 统计更新事件
        public event EventHandler<ApiEndpointStats>? StatsUpdated;

        public ApiPoolManager(LogService logService, ConfigService configService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        /// <summary>
        /// 初始化端点池（从配置加载）
        /// 内部方法，仅在未初始化或显式重置时调用
        /// </summary>
        private async Task InitializeInternalAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                var config = await _configService.LoadConfigAsync(cancellationToken);

                // 清理旧的信号量
                foreach (var semaphore in _endpointSemaphores.Values)
                {
                    semaphore?.Dispose();
                }
                _endpointSemaphores.Clear();
                _statsMap.Clear();

                // 为每个启用的端点创建统计和信号量
                foreach (var endpoint in config.ApiEndpoints.Where(e => e.IsEnabled))
                {
                    if (!_statsMap.ContainsKey(endpoint.Id))
                    {
                        _statsMap[endpoint.Id] = new ApiEndpointStats
                        {
                            EndpointId = endpoint.Id,
                            EndpointName = endpoint.Name
                        };
                    }

                    if (!_endpointSemaphores.ContainsKey(endpoint.Id))
                    {
                        _endpointSemaphores[endpoint.Id] = new SemaphoreSlim(
                            endpoint.MaxConcurrent,
                            endpoint.MaxConcurrent);
                    }
                }

                _isInitialized = true;

                await _logService.LogAsync(
                    $"API池已初始化，共 {_statsMap.Count} 个端点可用",
                    LogLevel.Info);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 确保池已初始化（幂等操作，只初始化一次）
        /// </summary>
        public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            // 快速路径：已初始化则直接返回
            if (_isInitialized)
            {
                return;
            }

            // 慢速路径：获取锁并检查
            await _lock.WaitAsync(cancellationToken);
            try
            {
                // 双重检查锁定模式
                if (!_isInitialized)
                {
                    _lock.Release(); // 释放锁，让 InitializeInternalAsync 自己获取
                    await InitializeInternalAsync(cancellationToken);
                    return;
                }
            }
            finally
            {
                if (_lock.CurrentCount == 0)
                {
                    _lock.Release();
                }
            }
        }

        /// <summary>
        /// 重置池（用于配置更改后重新初始化）
        /// </summary>
        public async Task ResetAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _isInitialized = false;
            }
            finally
            {
                _lock.Release();
            }

            await InitializeInternalAsync(cancellationToken);
        }

        /// <summary>
        /// 更新端点配置（保留统计数据）
        /// 用于配置更改时更新信号量，但不清空统计历史
        /// </summary>
        public async Task UpdateEndpointsAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                var config = await _configService.LoadConfigAsync(cancellationToken);

                // 只清理信号量，保留统计数据
                foreach (var semaphore in _endpointSemaphores.Values)
                {
                    semaphore?.Dispose();
                }
                _endpointSemaphores.Clear();

                // 为启用的端点重新创建信号量
                foreach (var endpoint in config.ApiEndpoints.Where(e => e.IsEnabled))
                {
                    _endpointSemaphores[endpoint.Id] = new SemaphoreSlim(
                        endpoint.MaxConcurrent,
                        endpoint.MaxConcurrent);

                    // 如果统计数据不存在才创建（保留已有的）
                    if (!_statsMap.ContainsKey(endpoint.Id))
                    {
                        _statsMap[endpoint.Id] = new ApiEndpointStats
                        {
                            EndpointId = endpoint.Id,
                            EndpointName = endpoint.Name
                        };
                    }
                }

                await _logService.LogAsync(
                    $"端点配置已更新，统计数据保留（{_statsMap.Count}个端点）",
                    LogLevel.Info);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 获取最优端点（基于动态权重和可用性）
        /// </summary>
        /// <returns>返回端点配置和信号量，如果没有可用端点返回null</returns>
        public async Task<(ApiEndpoint? Endpoint, SemaphoreSlim? Semaphore)> AcquireEndpointAsync(
            CancellationToken cancellationToken = default)
        {
            var config = await _configService.LoadConfigAsync(cancellationToken);
            var enabledEndpoints = config.ApiEndpoints.Where(e => e.IsEnabled).ToList();

            if (enabledEndpoints.Count == 0)
            {
                await _logService.LogAsync("没有可用的API端点", LogLevel.Warning);
                return (null, null);
            }

            // 如果只有一个端点，直接返回
            if (enabledEndpoints.Count == 1)
            {
                var endpoint = enabledEndpoints[0];
                var semaphore = await GetOrCreateSemaphoreAsync(endpoint);
                await semaphore.WaitAsync(cancellationToken);

                var stats = GetOrCreateStats(endpoint);
                stats.IncrementActiveRequests();

                return (endpoint, semaphore);
            }

            // 多个端点：基于权重选择
            ApiEndpoint? selectedEndpoint = null;
            SemaphoreSlim? selectedSemaphore = null;

            // 尝试最多3次选择（避免死锁）
            for (int attempt = 0; attempt < 3; attempt++)
            {
                selectedEndpoint = SelectBestEndpoint(enabledEndpoints);
                if (selectedEndpoint == null) break;

                selectedSemaphore = await GetOrCreateSemaphoreAsync(selectedEndpoint);

                // 尝试获取信号量（非阻塞）
                if (await selectedSemaphore.WaitAsync(0, cancellationToken))
                {
                    var stats = GetOrCreateStats(selectedEndpoint);
                    stats.IncrementActiveRequests();

                    await _logService.LogAsync(
                        $"选择端点: {selectedEndpoint.Name} (权重: {GetOrCreateStats(selectedEndpoint).DynamicWeight:F2})",
                        LogLevel.Debug);

                    return (selectedEndpoint, selectedSemaphore);
                }
                else
                {
                    // 该端点已达并发上限，尝试下一个
                    await _logService.LogAsync(
                        $"端点 {selectedEndpoint.Name} 已达并发上限，尝试其他端点",
                        LogLevel.Debug);

                    // 从列表中移除已满的端点
                    enabledEndpoints.Remove(selectedEndpoint);
                    if (enabledEndpoints.Count == 0) break;
                }
            }

            // 所有端点都满了，等待权重最高的端点
            selectedEndpoint = SelectBestEndpoint(config.ApiEndpoints.Where(e => e.IsEnabled).ToList());
            if (selectedEndpoint == null)
            {
                await _logService.LogAsync("无法选择可用端点", LogLevel.Error);
                return (null, null);
            }

            selectedSemaphore = await GetOrCreateSemaphoreAsync(selectedEndpoint);
            await selectedSemaphore.WaitAsync(cancellationToken);

            var finalStats = GetOrCreateStats(selectedEndpoint);
            finalStats.IncrementActiveRequests();

            await _logService.LogAsync(
                $"等待后选择端点: {selectedEndpoint.Name}",
                LogLevel.Debug);

            return (selectedEndpoint, selectedSemaphore);
        }

        /// <summary>
        /// 释放端点（记录结果并释放信号量）
        /// </summary>
        public async Task ReleaseEndpointAsync(
            ApiEndpoint endpoint,
            SemaphoreSlim semaphore,
            bool success,
            double responseTimeMs)
        {
            if (endpoint == null || semaphore == null) return;

            try
            {
                var stats = GetOrCreateStats(endpoint);
                stats.RecordRequest(success, responseTimeMs);
                stats.DecrementActiveRequests();

                // 触发统计更新事件
                StatsUpdated?.Invoke(this, stats);

                await _logService.LogAsync(
                    $"释放端点: {endpoint.Name} - {(success ? "成功" : "失败")} ({responseTimeMs:F0}ms)",
                    LogLevel.Debug);
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// 选择最佳端点（基于综合权重）
        /// </summary>
        private ApiEndpoint? SelectBestEndpoint(List<ApiEndpoint> endpoints)
        {
            if (endpoints.Count == 0) return null;
            if (endpoints.Count == 1) return endpoints[0];

            // 计算每个端点的综合权重
            var weightedEndpoints = endpoints.Select(endpoint =>
            {
                var stats = GetOrCreateStats(endpoint);
                double dynamicWeight = stats.DynamicWeight;

                // 混合手动权重和动态权重
                double manualWeight = endpoint.Weight / 100.0; // 归一化到0-1
                double finalWeight = dynamicWeight * 0.7 + manualWeight * 0.3;

                // 如果端点已达并发上限，权重降为0
                var semaphore = _endpointSemaphores.ContainsKey(endpoint.Id)
                    ? _endpointSemaphores[endpoint.Id]
                    : null;

                if (semaphore != null && semaphore.CurrentCount == 0)
                {
                    finalWeight *= 0.1; // 大幅降低权重但不完全排除
                }

                return new { Endpoint = endpoint, Weight = finalWeight };
            }).ToList();

            // 选择权重最高的端点
            var best = weightedEndpoints.OrderByDescending(w => w.Weight).FirstOrDefault();
            return best?.Endpoint;
        }

        /// <summary>
        /// 获取或创建统计对象
        /// </summary>
        private ApiEndpointStats GetOrCreateStats(ApiEndpoint endpoint)
        {
            if (!_statsMap.ContainsKey(endpoint.Id))
            {
                _statsMap[endpoint.Id] = new ApiEndpointStats
                {
                    EndpointId = endpoint.Id,
                    EndpointName = endpoint.Name
                };
            }
            return _statsMap[endpoint.Id];
        }

        /// <summary>
        /// 获取或创建信号量
        /// </summary>
        private async Task<SemaphoreSlim> GetOrCreateSemaphoreAsync(ApiEndpoint endpoint)
        {
            await _lock.WaitAsync();
            try
            {
                if (!_endpointSemaphores.ContainsKey(endpoint.Id))
                {
                    _endpointSemaphores[endpoint.Id] = new SemaphoreSlim(
                        endpoint.MaxConcurrent,
                        endpoint.MaxConcurrent);
                }
                return _endpointSemaphores[endpoint.Id];
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 获取所有端点的统计信息
        /// </summary>
        public List<ApiEndpointStats> GetAllStats()
        {
            lock (_statsMap)
            {
                return _statsMap.Values.ToList();
            }
        }

        /// <summary>
        /// 获取指定端点的统计信息
        /// </summary>
        public ApiEndpointStats? GetStats(string endpointId)
        {
            lock (_statsMap)
            {
                return _statsMap.ContainsKey(endpointId) ? _statsMap[endpointId] : null;
            }
        }

        /// <summary>
        /// 重置所有统计信息
        /// </summary>
        public void ResetAllStats()
        {
            lock (_statsMap)
            {
                foreach (var stats in _statsMap.Values)
                {
                    stats.Reset();
                }
            }
        }

        /// <summary>
        /// 健康检查（可定期调用）
        /// </summary>
        public async Task RunHealthCheckAsync(CancellationToken cancellationToken = default)
        {
            var config = await _configService.LoadConfigAsync(cancellationToken);
            var enabledEndpoints = config.ApiEndpoints.Where(e => e.IsEnabled).ToList();

            foreach (var endpoint in enabledEndpoints)
            {
                var stats = GetOrCreateStats(endpoint);

                // 检查是否长时间无响应
                if (stats.LastRequestTime != DateTime.MinValue &&
                    (DateTime.Now - stats.LastRequestTime).TotalMinutes > 5 &&
                    stats.ActiveRequests > 0)
                {
                    await _logService.LogAsync(
                        $"端点 {endpoint.Name} 可能存在挂起请求（{stats.ActiveRequests}个）",
                        LogLevel.Warning);
                }
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _lock?.Dispose();
            foreach (var semaphore in _endpointSemaphores.Values)
            {
                semaphore?.Dispose();
            }
        }
    }
}
