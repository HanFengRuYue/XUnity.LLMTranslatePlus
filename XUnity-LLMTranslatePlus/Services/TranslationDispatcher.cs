using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Models;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 翻译任务分发器
    /// 负责将翻译任务智能分配给多个API端点并发执行
    /// </summary>
    public class TranslationDispatcher
    {
        private readonly ApiPoolManager _poolManager;
        private readonly ApiClient _apiClient;
        private readonly LogService _logService;

        public TranslationDispatcher(
            ApiPoolManager poolManager,
            ApiClient apiClient,
            LogService logService)
        {
            _poolManager = poolManager ?? throw new ArgumentNullException(nameof(poolManager));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        /// <summary>
        /// 分发翻译任务（使用负载均衡）
        /// </summary>
        /// <param name="text">待翻译文本</param>
        /// <param name="systemPrompt">系统提示词</param>
        /// <param name="config">应用配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>翻译结果</returns>
        public async Task<string> DispatchTranslationAsync(
            string text,
            string systemPrompt,
            AppConfig config,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            // 确保池已初始化（幂等操作，只初始化一次）
            await _poolManager.EnsureInitializedAsync(cancellationToken);

            Exception? lastException = null;
            int attemptCount = 0;
            const int maxAttempts = 5; // 最多尝试5个不同的端点

            while (attemptCount < maxAttempts)
            {
                attemptCount++;
                cancellationToken.ThrowIfCancellationRequested();

                // 1. 从池中获取最优端点
                var (endpoint, semaphore) = await _poolManager.AcquireEndpointAsync(cancellationToken);

                if (endpoint == null || semaphore == null)
                {
                    await _logService.LogAsync(
                        "无法获取可用的API端点",
                        LogLevel.Error);

                    throw new Exception("没有可用的API端点");
                }

                var stopwatch = Stopwatch.StartNew();
                string translatedText = "";

                try
                {
                    await _logService.LogAsync(
                        $"[分发器] 使用端点 {endpoint.Name} 翻译: {GetPreview(text, 20)}...",
                        LogLevel.Debug);

                    // 2. 使用选定的端点翻译
                    translatedText = await _apiClient.TranslateAsync(
                        text,
                        systemPrompt,
                        endpoint,
                        cancellationToken);

                    stopwatch.Stop();

                    // 3. 记录成功
                    await _poolManager.ReleaseEndpointAsync(
                        endpoint,
                        semaphore,
                        success: true,
                        responseTimeMs: stopwatch.Elapsed.TotalMilliseconds);

                    await _logService.LogAsync(
                        $"[分发器] 翻译成功: {endpoint.Name} ({stopwatch.ElapsedMilliseconds}ms)",
                        LogLevel.Debug);

                    return translatedText;
                }
                catch (OperationCanceledException)
                {
                    // 用户取消，直接抛出
                    stopwatch.Stop();
                    await _poolManager.ReleaseEndpointAsync(
                        endpoint,
                        semaphore,
                        success: false,
                        responseTimeMs: stopwatch.Elapsed.TotalMilliseconds);

                    throw;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    lastException = ex;

                    // 记录失败
                    await _poolManager.ReleaseEndpointAsync(
                        endpoint,
                        semaphore,
                        success: false,
                        responseTimeMs: stopwatch.Elapsed.TotalMilliseconds);

                    await _logService.LogAsync(
                        $"[分发器] 端点 {endpoint.Name} 翻译失败: {ex.Message}，尝试下一个端点 (尝试 {attemptCount}/{maxAttempts})",
                        LogLevel.Warning);

                    // 如果还有重试机会，等待一小段时间后继续
                    if (attemptCount < maxAttempts)
                    {
                        await Task.Delay(500 * attemptCount, cancellationToken);
                        continue;
                    }
                }
            }

            // 所有端点都失败了
            string errorMsg = $"所有API端点均翻译失败 (尝试了 {attemptCount} 次)";
            await _logService.LogAsync(errorMsg, LogLevel.Error);

            throw new Exception(
                errorMsg,
                lastException);
        }

        /// <summary>
        /// 获取文本预览
        /// </summary>
        private static string GetPreview(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (text.Length <= maxLength)
                return text;

            return text.AsSpan(0, maxLength).ToString();
        }
    }
}
