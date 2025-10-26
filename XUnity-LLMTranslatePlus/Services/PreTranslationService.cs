using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Utils;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 预翻译服务
    /// 协调整个预翻译流程：扫描→提取→翻译→合并
    /// </summary>
    public class PreTranslationService : IDisposable
    {
        private readonly AssetScannerService _scannerService;
        private readonly TranslationService _translationService;
        private readonly ConfigService _configService;
        private readonly LogService _logService;

        public PreTranslationService(
            AssetScannerService scannerService,
            TranslationService translationService,
            ConfigService configService,
            LogService logService)
        {
            _scannerService = scannerService;
            _translationService = translationService;
            _configService = configService;
            _logService = logService;
        }

        /// <summary>
        /// 执行完整的预翻译流程
        /// </summary>
        public async Task<PreTranslationResult> ExecutePreTranslationAsync(
            string gameDirectory,
            List<ExtractedTextEntry> extractedTexts,
            IProgress<PreTranslationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new PreTranslationResult();

            try
            {
                var config = _configService.GetCurrentConfig();

                await _logService.LogAsync("开始预翻译流程", LogLevel.Info);

                // 步骤 1: 加载现有翻译文件
                progress?.Report(new PreTranslationProgress
                {
                    CurrentStep = "加载现有翻译",
                    ProgressPercentage = 10
                });

                var existingTranslations = await LoadExistingTranslationsAsync(gameDirectory);
                result.ExistingTranslationsCount = existingTranslations.Count;

                await _logService.LogAsync($"已加载 {existingTranslations.Count} 条现有翻译", LogLevel.Info);

                // 步骤 2: 过滤需要翻译的文本
                progress?.Report(new PreTranslationProgress
                {
                    CurrentStep = "过滤文本",
                    ProgressPercentage = 20
                });

                var textsToTranslate = FilterTextsToTranslate(extractedTexts, existingTranslations, config);
                result.TextsToTranslateCount = textsToTranslate.Count;

                await _logService.LogAsync($"需要翻译 {textsToTranslate.Count} 条文本", LogLevel.Info);

                if (textsToTranslate.Count == 0)
                {
                    await _logService.LogAsync("没有需要翻译的文本", LogLevel.Info);
                    result.Success = true;
                    return result;
                }

                // 步骤 3: 批量翻译
                progress?.Report(new PreTranslationProgress
                {
                    CurrentStep = "翻译文本",
                    ProgressPercentage = 30
                });

                var translations = await TranslateBatchAsync(
                    textsToTranslate,
                    config,
                    (p) => progress?.Report(new PreTranslationProgress
                    {
                        CurrentStep = $"翻译中 ({p.TranslatedCount}/{p.TotalCount})",
                        ProgressPercentage = 30 + (int)(p.ProgressPercentage * 0.6)
                    }),
                    cancellationToken);

                result.SuccessfulTranslations = translations.Count;
                result.FailedTranslations = textsToTranslate.Count - translations.Count;

                // 步骤 4: 合并翻译
                progress?.Report(new PreTranslationProgress
                {
                    CurrentStep = "合并翻译结果",
                    ProgressPercentage = 90
                });

                await MergeTranslationsAsync(gameDirectory, translations, existingTranslations, config);
                result.FinalTranslationsCount = existingTranslations.Count + translations.Count;

                progress?.Report(new PreTranslationProgress
                {
                    CurrentStep = "完成",
                    ProgressPercentage = 100
                });

                result.Success = true;
                await _logService.LogAsync("预翻译流程完成", LogLevel.Info);
            }
            catch (OperationCanceledException)
            {
                await _logService.LogAsync("预翻译已取消", LogLevel.Warning);
                result.Success = false;
                throw;
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"预翻译失败: {ex.Message}", LogLevel.Error);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                throw;
            }

            return result;
        }

        /// <summary>
        /// 加载现有的 XUnity 翻译文件
        /// </summary>
        private async Task<Dictionary<string, string>> LoadExistingTranslationsAsync(string gameDirectory)
        {
            var translations = new Dictionary<string, string>();

            try
            {
                var config = _configService.GetCurrentConfig();
                string? filePath = null;

                // 查找翻译文件（与 FileMonitorService 相同的逻辑）
                if (!string.IsNullOrWhiteSpace(config.ManualTranslationFilePath))
                {
                    if (File.Exists(config.ManualTranslationFilePath))
                    {
                        filePath = config.ManualTranslationFilePath;
                    }
                }

                if (filePath == null)
                {
                    filePath = FindTranslationFile(gameDirectory);
                }

                if (filePath == null || !File.Exists(filePath))
                {
                    await _logService.LogAsync("未找到现有翻译文件", LogLevel.Warning);
                    return translations;
                }

                // 解析翻译文件
                var parser = new TextFileParser();
                var entries = await parser.ParseFileAsync(filePath);

                foreach (var entry in entries)
                {
                    if (entry.IsTranslated && !string.IsNullOrWhiteSpace(entry.Value))
                    {
                        translations[entry.Key] = entry.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"加载现有翻译失败: {ex.Message}", LogLevel.Warning);
            }

            return translations;
        }

        /// <summary>
        /// 查找翻译文件（与 FileMonitorService 相同的逻辑）
        /// </summary>
        private string? FindTranslationFile(string gameDirectory)
        {
            string bepInExPath = Path.Combine(gameDirectory, "BepInEx", "Translation");

            if (!Directory.Exists(bepInExPath))
                return null;

            var langFolders = Directory.GetDirectories(bepInExPath);

            foreach (var langFolder in langFolders)
            {
                string textFolder = Path.Combine(langFolder, "Text");
                if (Directory.Exists(textFolder))
                {
                    string translationFile = Path.Combine(textFolder, "_AutoGeneratedTranslations.txt");
                    if (File.Exists(translationFile))
                        return translationFile;
                }
            }

            return null;
        }

        /// <summary>
        /// 过滤需要翻译的文本
        /// </summary>
        private List<string> FilterTextsToTranslate(
            List<ExtractedTextEntry> extractedTexts,
            Dictionary<string, string> existingTranslations,
            AppConfig config)
        {
            var assetConfig = config.AssetExtraction;

            return extractedTexts
                .Where(entry => entry.IsSelected) // 仅翻译用户选择的文本
                .Select(entry => entry.Text)
                .Distinct() // 去重
                .Where(text =>
                    assetConfig.OverwriteExisting || !existingTranslations.ContainsKey(text))
                .ToList();
        }

        /// <summary>
        /// 批量翻译文本
        /// </summary>
        private async Task<Dictionary<string, string>> TranslateBatchAsync(
            List<string> texts,
            AppConfig config,
            Action<BatchTranslationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var translations = new Dictionary<string, string>();
            var channel = Channel.CreateUnbounded<string>();
            var resultChannel = Channel.CreateUnbounded<(string, string)>();

            // 从所有启用的API端点计算总并发容量（与 FileMonitorService 一致）
            var enabledEndpoints = config.ApiEndpoints.Where(e => e.IsEnabled).ToList();
            int concurrency = enabledEndpoints.Sum(e => e.MaxConcurrent);
            concurrency = Math.Min(concurrency, 100); // 上限100，防止过度消耗资源

            // 如果没有启用的端点或并发数为0，使用默认值3
            if (concurrency == 0)
            {
                concurrency = 3;
                await _logService.LogAsync("未配置启用的API端点，使用默认并发数: 3", LogLevel.Warning);
            }

            await _logService.LogAsync($"批量翻译使用 {concurrency} 个并发任务（基于 {enabledEndpoints.Count} 个API端点）", LogLevel.Info);

            // 启动消费者任务
            var consumerTasks = new List<Task>();

            for (int i = 0; i < concurrency; i++)
            {
                int consumerId = i + 1;
                consumerTasks.Add(Task.Run(async () =>
                {
                    await foreach (var text in channel.Reader.ReadAllAsync(cancellationToken))
                    {
                        try
                        {
                            var translated = await _translationService.TranslateTextAsync(text, config, cancellationToken);

                            if (!string.Equals(text, translated, StringComparison.Ordinal))
                            {
                                await resultChannel.Writer.WriteAsync((text, translated), cancellationToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            await _logService.LogAsync(
                                $"[消费者{consumerId}] 翻译失败: {ex.Message}",
                                LogLevel.Error);
                        }
                    }
                }, cancellationToken));
            }

            // 生产者任务：写入待翻译文本
            var producerTask = Task.Run(async () =>
            {
                foreach (var text in texts)
                {
                    await channel.Writer.WriteAsync(text, cancellationToken);
                }
                channel.Writer.Complete();
            }, cancellationToken);

            // 结果收集任务
            var collectorTask = Task.Run(async () =>
            {
                int count = 0;
                await foreach (var (original, translated) in resultChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    translations[original] = translated;
                    count++;

                    progress?.Invoke(new BatchTranslationProgress
                    {
                        TotalCount = texts.Count,
                        TranslatedCount = count,
                        ProgressPercentage = (double)count / texts.Count * 100
                    });
                }
            }, cancellationToken);

            // 等待所有消费者完成
            await Task.WhenAll(consumerTasks);
            resultChannel.Writer.Complete();

            // 等待结果收集完成
            await collectorTask;

            return translations;
        }

        /// <summary>
        /// 合并翻译到 XUnity 翻译文件
        /// </summary>
        private async Task MergeTranslationsAsync(
            string gameDirectory,
            Dictionary<string, string> newTranslations,
            Dictionary<string, string> existingTranslations,
            AppConfig config)
        {
            string? filePath = null;

            // 查找翻译文件
            if (!string.IsNullOrWhiteSpace(config.ManualTranslationFilePath))
            {
                filePath = config.ManualTranslationFilePath;
            }
            else
            {
                filePath = FindTranslationFile(gameDirectory);
            }

            // 如果找不到文件，创建默认路径
            if (filePath == null)
            {
                filePath = CreateDefaultTranslationFilePath(gameDirectory, config.TargetLanguage);
                await _logService.LogAsync($"未找到现有翻译文件，将创建新文件: {filePath}", LogLevel.Warning);
            }

            // 合并翻译
            var mergedTranslations = new Dictionary<string, string>(existingTranslations);
            foreach (var kvp in newTranslations)
            {
                mergedTranslations[kvp.Key] = kvp.Value;
            }

            await _logService.LogAsync($"准备保存 {mergedTranslations.Count} 条翻译（新增 {newTranslations.Count} 条）", LogLevel.Info);

            // 写入文件
            var parser = new TextFileParser();

            // 如果文件已存在，尝试解析以保留格式
            if (File.Exists(filePath))
            {
                try
                {
                    await parser.ParseFileAsync(filePath);
                    // 更新翻译
                    parser.UpdateTranslations(mergedTranslations);
                    // 保存（保留格式）
                    await parser.SaveFileAsync(filePath);
                }
                catch (Exception ex)
                {
                    // 如果解析失败，使用直接保存方法
                    await _logService.LogAsync($"解析现有文件失败，将直接覆盖: {ex.Message}", LogLevel.Warning);
                    await parser.SaveTranslationsDirectAsync(filePath, mergedTranslations);
                }
            }
            else
            {
                // 文件不存在，直接创建新文件
                await parser.SaveTranslationsDirectAsync(filePath, mergedTranslations);
            }

            await _logService.LogAsync($"✓ 翻译已保存到文件: {filePath}", LogLevel.Info);
            await _logService.LogAsync($"✓ 总计 {mergedTranslations.Count} 条翻译（新增 {newTranslations.Count} 条，已有 {existingTranslations.Count} 条）", LogLevel.Info);
        }

        /// <summary>
        /// 创建默认的翻译文件路径
        /// </summary>
        private string CreateDefaultTranslationFilePath(string gameDirectory, string targetLanguage)
        {
            // 创建 BepInEx/Translation/{语言}/Text 目录结构
            string translationDir = Path.Combine(gameDirectory, "BepInEx", "Translation", targetLanguage, "Text");
            Directory.CreateDirectory(translationDir);
            return Path.Combine(translationDir, "_AutoGeneratedTranslations.txt");
        }

        public void Dispose()
        {
            // No resources to dispose
        }
    }

    /// <summary>
    /// 预翻译进度信息
    /// </summary>
    public class PreTranslationProgress
    {
        public string CurrentStep { get; set; } = "";
        public double ProgressPercentage { get; set; }
    }

    /// <summary>
    /// 批量翻译进度信息
    /// </summary>
    public class BatchTranslationProgress
    {
        public int TotalCount { get; set; }
        public int TranslatedCount { get; set; }
        public double ProgressPercentage { get; set; }
    }

    /// <summary>
    /// 预翻译结果
    /// </summary>
    public class PreTranslationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public int ExistingTranslationsCount { get; set; }
        public int TextsToTranslateCount { get; set; }
        public int SuccessfulTranslations { get; set; }
        public int FailedTranslations { get; set; }
        public int FinalTranslationsCount { get; set; }
    }
}
