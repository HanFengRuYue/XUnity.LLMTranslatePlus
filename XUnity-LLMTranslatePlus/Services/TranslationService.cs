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
        private readonly SmartTerminologyService? _smartTerminologyService;

        // 统计信息 - 使用 HashSet 追踪已翻译的唯一文本，避免重复计数
        private readonly HashSet<string> _translatedTexts = new HashSet<string>();
        private readonly object _statsLock = new object();

        public int TotalTranslated
        {
            get
            {
                lock (_statsLock)
                {
                    return _translatedTexts.Count;
                }
            }
        }
        public int TotalFailed { get; private set; }
        public int TotalSkipped { get; private set; }

        // 正在翻译中的数量（已发送给API等待返回）
        private int _translatingCount = 0;
        private readonly object _translatingLock = new object();
        public int TranslatingCount
        {
            get
            {
                lock (_translatingLock)
                {
                    return _translatingCount;
                }
            }
        }

        // 上下文缓存及其锁
        private readonly List<string> _contextCache = new List<string>();
        private readonly object _contextLock = new object();

        public event EventHandler<TranslationProgressEventArgs>? ProgressUpdated;

        public TranslationService(
            ApiClient apiClient,
            TerminologyService terminologyService,
            LogService logService,
            ConfigService configService,
            SmartTerminologyService? smartTerminologyService = null)
        {
            _apiClient = apiClient;
            _terminologyService = terminologyService;
            _logService = logService;
            _configService = configService;
            _smartTerminologyService = smartTerminologyService;
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

            // 增加正在翻译的计数
            lock (_translatingLock)
            {
                _translatingCount++;
            }
            NotifyProgress();

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
                string contextReference = BuildContextReference(config);

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
                AddToContext(originalText, translatedText, config);

                // 9. 智能术语提取（异步，不阻塞翻译流程）
                if (_smartTerminologyService != null && config.EnableSmartTerminology)
                {
                    // 使用 Task.Run 在后台执行，不等待结果
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _smartTerminologyService.ExtractAndAddTermsAsync(
                                originalText,
                                translatedText,
                                config,
                                cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            // 智能提取失败不影响翻译，只记录日志
                            await _logService.LogAsync($"[智能术语] 后台提取失败: {ex.Message}", LogLevel.Warning);
                        }
                    }, cancellationToken);
                }

                // 记录到已翻译集合（去重统计）
                lock (_statsLock)
                {
                    _translatedTexts.Add(originalText);
                }
                NotifyProgress();

                await _logService.LogAsync($"翻译完成: {translatedText}", LogLevel.Debug);

                return translatedText;
            }
            catch (Exception ex)
            {
                lock (_statsLock)
                {
                    TotalFailed++;
                }
                NotifyProgress();

                await _logService.LogAsync($"翻译失败: {originalText} - {ex.Message}", LogLevel.Error);
                throw;
            }
            finally
            {
                // 无论成功还是失败，都要减少正在翻译的计数
                lock (_translatingLock)
                {
                    _translatingCount--;
                }
                NotifyProgress();
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
        /// 构建上下文参考（线程安全）
        /// </summary>
        private string BuildContextReference(AppConfig config)
        {
            // 检查是否启用上下文功能
            if (!config.EnableContext)
            {
                return "无";
            }

            lock (_contextLock)
            {
                if (_contextCache.Count == 0)
                {
                    return "无";
                }

                // 在锁内创建副本避免迭代异常
                return string.Join("\n", _contextCache.TakeLast(config.ContextLines));
            }
        }

        /// <summary>
        /// 添加到上下文缓存（线程安全）
        /// </summary>
        private void AddToContext(string original, string translated, AppConfig config)
        {
            lock (_contextLock)
            {
                string contextLine = $"{original} -> {translated}";
                _contextCache.Add(contextLine);

                // 使用配置的值保持缓存大小
                while (_contextCache.Count > config.ContextLines)
                {
                    _contextCache.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// 清空上下文缓存（线程安全）
        /// </summary>
        public void ClearContext()
        {
            lock (_contextLock)
            {
                _contextCache.Clear();
            }
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void ResetStatistics()
        {
            lock (_statsLock)
            {
                _translatedTexts.Clear();
                TotalFailed = 0;
                TotalSkipped = 0;
            }
            lock (_translatingLock)
            {
                _translatingCount = 0;
            }
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
                TotalSkipped = TotalSkipped,
                TranslatingCount = TranslatingCount
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
        public int TranslatingCount { get; set; }
    }
}

