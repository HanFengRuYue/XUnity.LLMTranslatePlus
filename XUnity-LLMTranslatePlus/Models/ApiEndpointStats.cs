using System;
using System.Threading;

namespace XUnity_LLMTranslatePlus.Models
{
    /// <summary>
    /// API 端点性能统计
    /// </summary>
    public class ApiEndpointStats
    {
        private readonly object _lock = new object();

        /// <summary>
        /// 端点ID
        /// </summary>
        public string EndpointId { get; set; } = "";

        /// <summary>
        /// 端点名称（用于显示）
        /// </summary>
        public string EndpointName { get; set; } = "";

        // 实时统计
        private int _activeRequests = 0;
        /// <summary>
        /// 当前活跃请求数
        /// </summary>
        public int ActiveRequests
        {
            get
            {
                lock (_lock)
                {
                    return _activeRequests;
                }
            }
        }

        /// <summary>
        /// 总请求数
        /// </summary>
        public long TotalRequests { get; private set; } = 0;

        /// <summary>
        /// 成功请求数
        /// </summary>
        public long SuccessCount { get; private set; } = 0;

        /// <summary>
        /// 失败请求数
        /// </summary>
        public long FailureCount { get; private set; } = 0;

        /// <summary>
        /// 平均响应时间（毫秒，使用指数移动平均EMA）
        /// </summary>
        public double AverageResponseTime { get; private set; } = 0;

        /// <summary>
        /// 最后请求时间
        /// </summary>
        public DateTime LastRequestTime { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// 最后成功时间
        /// </summary>
        public DateTime LastSuccessTime { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// 最后失败时间
        /// </summary>
        public DateTime LastFailureTime { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// 成功率（0-1）
        /// </summary>
        public double SuccessRate
        {
            get
            {
                lock (_lock)
                {
                    if (TotalRequests == 0) return 1.0; // 没有请求时默认为100%
                    return (double)SuccessCount / TotalRequests;
                }
            }
        }

        /// <summary>
        /// 当前吞吐量（每秒完成请求数）
        /// </summary>
        public double CurrentThroughput
        {
            get
            {
                lock (_lock)
                {
                    if (AverageResponseTime <= 0) return 0;
                    return 1000.0 / AverageResponseTime; // 每秒完成数
                }
            }
        }

        /// <summary>
        /// 动态权重（基于性能自动计算，0-1）
        /// </summary>
        public double DynamicWeight
        {
            get
            {
                lock (_lock)
                {
                    // 响应时间归一化（越快权重越高，使用对数缩放）
                    double normalizedSpeed = AverageResponseTime > 0
                        ? 1.0 / (1.0 + Math.Log(1.0 + AverageResponseTime / 1000.0))
                        : 1.0;

                    // 成功率权重
                    double successWeight = SuccessRate;

                    // 综合权重：速度40% + 成功率60%
                    double weight = normalizedSpeed * 0.4 + successWeight * 0.6;

                    return Math.Max(0, Math.Min(1.0, weight)); // 限制在0-1之间
                }
            }
        }

        /// <summary>
        /// 增加活跃请求数
        /// </summary>
        public void IncrementActiveRequests()
        {
            lock (_lock)
            {
                Interlocked.Increment(ref _activeRequests);
                LastRequestTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 减少活跃请求数
        /// </summary>
        public void DecrementActiveRequests()
        {
            lock (_lock)
            {
                Interlocked.Decrement(ref _activeRequests);
            }
        }

        /// <summary>
        /// 记录请求结果
        /// </summary>
        /// <param name="success">是否成功</param>
        /// <param name="responseTime">响应时间（毫秒）</param>
        public void RecordRequest(bool success, double responseTime)
        {
            lock (_lock)
            {
                TotalRequests++;

                if (success)
                {
                    SuccessCount++;
                    LastSuccessTime = DateTime.Now;

                    // 使用指数移动平均（EMA）平滑响应时间
                    // alpha = 0.3 意味着新数据占30%权重
                    double alpha = 0.3;
                    if (AverageResponseTime == 0)
                    {
                        AverageResponseTime = responseTime;
                    }
                    else
                    {
                        AverageResponseTime = alpha * responseTime + (1 - alpha) * AverageResponseTime;
                    }
                }
                else
                {
                    FailureCount++;
                    LastFailureTime = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _activeRequests = 0;
                TotalRequests = 0;
                SuccessCount = 0;
                FailureCount = 0;
                AverageResponseTime = 0;
                LastRequestTime = DateTime.MinValue;
                LastSuccessTime = DateTime.MinValue;
                LastFailureTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// 获取统计摘要
        /// </summary>
        public string GetSummary()
        {
            lock (_lock)
            {
                return $"{EndpointName}: {SuccessRate:P1} 成功率, {AverageResponseTime:F0}ms 平均, {ActiveRequests} 活跃";
            }
        }
    }
}
