using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Utils;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 翻译服务
    /// </summary>
    public class TranslationService
    {
        private readonly ApiClient _apiClient;
        private readonly TerminologyService _terminologyService;
        private readonly LogService _logService;
        private readonly ConfigService _configService;

        // 统计信息
        public int TotalTranslated { get; private set; }
        public int TotalFailed { get; private set; }
        public int TotalSkipped { get; private set; }

        // 上下文缓存
        private readonly List<string> _contextCache = new List<string>();
        private const int MaxContextLines = 10;

        public event EventHandler<TranslationProgressEventArgs>? ProgressUpdated;

        public TranslationService(
            ApiClient apiClient,
            TerminologyService terminologyService,
            LogService logService,
            ConfigService configService)
        {
            _apiClient = apiClient;
            _terminologyService = terminologyService;
            _logService = logService;
            _configService = configService;
        }

        /// <summary>
        /// 翻译单条文本
        /// </summary>
        public async Task<string> TranslateTextAsync(string originalText, AppConfig? config = null, CancellationToken cancellationToken = default)
        {
            config ??= _configService.GetCurrentConfig();

            if (string.IsNullOrWhiteSpace(originalText))
            {
                return originalText;
            }

            // 检查取消请求
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // 1. 提取特殊字符
                var extraction = config.PreserveSpecialChars
                    ? EscapeCharacterHandler.ExtractSpecialChars(originalText)
                    : new EscapeCharacterHandler.ExtractionResult { CleanedText = originalText };

                string textToTranslate = extraction.CleanedText;

                // 调试日志：显示提取结果
                if (config.PreserveSpecialChars && extraction.SpecialChars.Count > 0)
                {
                    await _logService.LogAsync($"[转义] 原文: {originalText}", LogLevel.Info);
                    await _logService.LogAsync($"[转义] 清理后: {textToTranslate}", LogLevel.Info);
                    await _logService.LogAsync($"[转义] 提取了 {extraction.SpecialChars.Count} 个特殊字符", LogLevel.Info);
                    foreach (var ch in extraction.SpecialChars)
                    {
                        await _logService.LogAsync($"[转义]   {ch.Placeholder} <- {ch.Original}", LogLevel.Info);
                    }
                }

                // 2. 构建术语参考
                string termsReference = _terminologyService.BuildTermsReference(textToTranslate);

                // 3. 构建上下文参考
                string contextReference = BuildContextReference();

                // 4. 构建系统提示词
                string systemPrompt = BuildSystemPrompt(
                    config.SystemPrompt,
                    config.TargetLanguage,
                    textToTranslate,
                    termsReference,
                    contextReference
                );

                await _logService.LogAsync($"开始翻译: {originalText}", LogLevel.Debug);

                // 5. 调用 API 翻译
                string translatedText = await _apiClient.TranslateAsync(textToTranslate, systemPrompt, config, cancellationToken);

                await _logService.LogAsync($"[API返回] {translatedText}", LogLevel.Info);

                // 6. 应用术语库（后处理）
                translatedText = _terminologyService.ApplyTerms(translatedText);

                // 7. 还原特殊字符
                if (config.PreserveSpecialChars && extraction.SpecialChars.Count > 0)
                {
                    await _logService.LogAsync($"[还原前] {translatedText}", LogLevel.Info);
                    translatedText = EscapeCharacterHandler.SmartRestoreSpecialChars(translatedText, extraction);
                    await _logService.LogAsync($"[还原后] {translatedText}", LogLevel.Info);
                }

                // 8. 添加到上下文缓存
                AddToContext(originalText, translatedText);

                TotalTranslated++;
                NotifyProgress();

                await _logService.LogAsync($"翻译完成: {translatedText}", LogLevel.Debug);

                return translatedText;
            }
            catch (Exception ex)
            {
                TotalFailed++;
                NotifyProgress();

                await _logService.LogAsync($"翻译失败: {originalText} - {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// 批量翻译文本
        /// </summary>
        public async Task<Dictionary<string, string>> TranslateBatchAsync(List<string> texts, AppConfig? config = null, CancellationToken cancellationToken = default)
        {
            config ??= _configService.GetCurrentConfig();

            var results = new Dictionary<string, string>();

            // 检查取消请求
            cancellationToken.ThrowIfCancellationRequested();

            // 使用局部信号量以控制并发，避免多次调用冲突
            using var semaphore = new SemaphoreSlim(config.MaxConcurrentTranslations, config.MaxConcurrentTranslations);

            var tasks = texts.Select(async text =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    string translated = await TranslateTextAsync(text, config, cancellationToken);
                    lock (results)
                    {
                        results[text] = translated;
                    }
                }
                catch (OperationCanceledException)
                {
                    // 取消操作，重新抛出
                    throw;
                }
                catch (Exception ex)
                {
                    await _logService.LogAsync($"批量翻译失败: {text} - {ex.Message}", LogLevel.Error);
                    lock (results)
                    {
                        results[text] = text; // 失败时保持原文
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            return results;
        }

        /// <summary>
        /// 构建系统提示词
        /// </summary>
        private string BuildSystemPrompt(
            string template,
            string targetLanguage,
            string originalText,
            string termsReference,
            string contextReference)
        {
            string prompt = template
                .Replace("{目标语言}", targetLanguage)
                .Replace("{原文}", originalText)
                .Replace("{术语}", termsReference)
                .Replace("{上下文}", contextReference);

            return prompt;
        }

        /// <summary>
        /// 构建上下文参考
        /// </summary>
        private string BuildContextReference()
        {
            if (_contextCache.Count == 0)
            {
                return "无";
            }

            return string.Join("\n", _contextCache.TakeLast(3));
        }

        /// <summary>
        /// 添加到上下文缓存
        /// </summary>
        private void AddToContext(string original, string translated)
        {
            string contextLine = $"{original} -> {translated}";
            
            _contextCache.Add(contextLine);

            // 保持缓存大小
            while (_contextCache.Count > MaxContextLines)
            {
                _contextCache.RemoveAt(0);
            }
        }

        /// <summary>
        /// 清空上下文缓存
        /// </summary>
        public void ClearContext()
        {
            _contextCache.Clear();
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void ResetStatistics()
        {
            TotalTranslated = 0;
            TotalFailed = 0;
            TotalSkipped = 0;
            NotifyProgress();
        }

        /// <summary>
        /// 通知进度更新
        /// </summary>
        private void NotifyProgress()
        {
            ProgressUpdated?.Invoke(this, new TranslationProgressEventArgs
            {
                TotalTranslated = TotalTranslated,
                TotalFailed = TotalFailed,
                TotalSkipped = TotalSkipped
            });
        }

        /// <summary>
        /// 获取统计摘要
        /// </summary>
        public string GetStatisticsSummary()
        {
            return $"已翻译: {TotalTranslated} | 失败: {TotalFailed} | 跳过: {TotalSkipped}";
        }
    }

    /// <summary>
    /// 翻译进度事件参数
    /// </summary>
    public class TranslationProgressEventArgs : EventArgs
    {
        public int TotalTranslated { get; set; }
        public int TotalFailed { get; set; }
        public int TotalSkipped { get; set; }
    }
}

