using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Models;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 智能术语提取服务
    /// </summary>
    public class SmartTerminologyService
    {
        private readonly ApiClient _apiClient;
        private readonly TerminologyService _terminologyService;
        private readonly ConfigService _configService;
        private readonly LogService _logService;
        private readonly TranslationDispatcher? _translationDispatcher;
        private readonly HashSet<string> _processedTexts = new HashSet<string>();
        private readonly object _processedLock = new object();

        // 智能提取的默认提示词模板
        private const string DefaultExtractionPrompt = @"请分析以下游戏文本的翻译，提取其中的专有名词（人名、地名、技能名、物品名、组织名等）。

原文：{原文}
译文：{译文}

要求：
1. 只提取专有名词，不要提取普通词汇
2. 确保提取的术语在原文和译文中是对应的
3. 返回 JSON 数组格式，每个元素包含 original 和 translation 字段
4. 只返回 JSON，不要其他内容
5. 如果没有专有名词，返回空数组 []

示例输出：
[{""original"": ""艾泽拉斯"", ""translation"": ""艾泽拉斯""}, {""original"": ""火球术"", ""translation"": ""火球术""}]";

        public SmartTerminologyService(
            ApiClient apiClient,
            TerminologyService terminologyService,
            ConfigService configService,
            LogService logService,
            TranslationDispatcher? translationDispatcher = null)
        {
            _apiClient = apiClient;
            _terminologyService = terminologyService;
            _configService = configService;
            _logService = logService;
            _translationDispatcher = translationDispatcher;
        }

        /// <summary>
        /// 从翻译结果中智能提取术语
        /// </summary>
        public async Task ExtractAndAddTermsAsync(
            string originalText,
            string translatedText,
            AppConfig config,
            CancellationToken cancellationToken = default)
        {
            // 检查是否启用智能提取
            if (!config.EnableSmartTerminology)
            {
                return;
            }

            // 检查是否已处理过该文本
            lock (_processedLock)
            {
                if (_processedTexts.Contains(originalText))
                {
                    return;
                }
                _processedTexts.Add(originalText);
            }

            try
            {
                // 构建提取提示词
                string extractionPrompt = DefaultExtractionPrompt
                    .Replace("{原文}", originalText)
                    .Replace("{译文}", translatedText);

                await _logService.LogAsync($"[智能术语] 开始提取: {originalText}", LogLevel.Debug);

                // 使用 TranslationDispatcher 进行多端点负载均衡调用
                string response;

                if (_translationDispatcher != null && config.ApiEndpoints != null && config.ApiEndpoints.Count > 0)
                {
                    // 使用多端点负载均衡模式（支持失败重试）
                    await _logService.LogAsync(
                        $"[智能术语] 使用多端点负载均衡模式 ({config.ApiEndpoints.Count(e => e.IsEnabled)}个可用端点)",
                        LogLevel.Debug);

                    response = await _translationDispatcher.DispatchTranslationAsync(
                        extractionPrompt,
                        "你是一个专业的术语提取助手。",
                        config,
                        cancellationToken);
                }
                else
                {
                    // 降级方案：使用单端点模式（向后兼容）
                    var endpoint = config.ApiEndpoints?.FirstOrDefault(e => e.IsEnabled);
                    if (endpoint == null)
                    {
                        await _logService.LogAsync("[智能术语] 没有可用的 API 端点", LogLevel.Warning);
                        return;
                    }

                    await _logService.LogAsync($"[智能术语] 使用单端点模式: {endpoint.Name}", LogLevel.Debug);
                    response = await _apiClient.TranslateAsync(
                        extractionPrompt,
                        "你是一个专业的术语提取助手。",
                        endpoint,
                        cancellationToken);
                }

                await _logService.LogAsync($"[智能术语] API 响应: {response}", LogLevel.Debug);

                // 解析响应
                var extractedTerms = ParseTerminologyResponse(response);

                // 添加到术语库
                if (extractedTerms.Count > 0)
                {
                    foreach (var term in extractedTerms)
                    {
                        // 检查是否已存在
                        if (!IsTermExists(term.Original))
                        {
                            var newTerm = new Term
                            {
                                Original = term.Original,
                                Translation = term.Translation,
                                Enabled = true
                            };

                            _terminologyService.AddTerm(newTerm);
                            await _logService.LogAsync(
                                $"[智能术语] 已添加: {term.Original} -> {term.Translation}",
                                LogLevel.Info);
                        }
                    }

                    // 保存术语库到当前选中的文件
                    string terminologyFilePath = GetCurrentTerminologyFilePath();
                    await _terminologyService.SaveTermsAsync(terminologyFilePath);
                }
            }
            catch (Exception ex)
            {
                // 智能提取失败不影响翻译流程，静默处理
                await _logService.LogAsync(
                    $"[智能术语] 提取失败: {ex.Message}",
                    LogLevel.Warning);
            }
        }

        /// <summary>
        /// 解析 AI 返回的术语 JSON
        /// </summary>
        private List<ExtractedTerm> ParseTerminologyResponse(string jsonResponse)
        {
            try
            {
                // 清理响应（移除可能的 markdown 代码块标记）
                string cleaned = jsonResponse.Trim();
                if (cleaned.StartsWith("```json"))
                {
                    cleaned = cleaned.Substring("```json".Length);
                }
                if (cleaned.StartsWith("```"))
                {
                    cleaned = cleaned.Substring("```".Length);
                }
                if (cleaned.EndsWith("```"))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - 3);
                }
                cleaned = cleaned.Trim();

                // 尝试找到 JSON 数组的开始和结束
                int startIndex = cleaned.IndexOf('[');
                int endIndex = cleaned.LastIndexOf(']');

                if (startIndex >= 0 && endIndex > startIndex)
                {
                    cleaned = cleaned.Substring(startIndex, endIndex - startIndex + 1);
                }

                // 解析 JSON
                var terms = JsonSerializer.Deserialize<List<ExtractedTerm>>(cleaned, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (terms == null || terms.Count == 0)
                {
                    return new List<ExtractedTerm>();
                }

                // 过滤无效术语
                int originalCount = terms.Count;
                var filteredTerms = terms.Where(t =>
                    // 基本验证
                    !string.IsNullOrWhiteSpace(t.Original) &&
                    !string.IsNullOrWhiteSpace(t.Translation) &&
                    t.Original.Length >= 2 &&
                    t.Translation.Length >= 2 &&
                    // 过滤包含 SPECIAL 占位符的术语
                    !t.Original.Contains("SPECIAL", StringComparison.OrdinalIgnoreCase) &&
                    !t.Translation.Contains("SPECIAL", StringComparison.OrdinalIgnoreCase) &&
                    // 过滤包含占位符标记的术语
                    !t.Original.Contains("【") && !t.Original.Contains("】") &&
                    !t.Translation.Contains("【") && !t.Translation.Contains("】") &&
                    // 过滤原文和译文完全相同的术语
                    !t.Original.Equals(t.Translation, StringComparison.Ordinal)
                ).ToList();

                int filteredCount = originalCount - filteredTerms.Count;
                if (filteredCount > 0)
                {
                    _logService.Log($"[智能术语] 过滤了 {filteredCount} 个无效术语", LogLevel.Debug);
                }

                return filteredTerms;
            }
            catch (Exception ex)
            {
                _logService.Log($"[智能术语] JSON 解析失败: {ex.Message}", LogLevel.Warning);
                return new List<ExtractedTerm>();
            }
        }

        /// <summary>
        /// 检查术语是否已存在
        /// </summary>
        private bool IsTermExists(string original)
        {
            var existingTerms = _terminologyService.GetTerms();
            return existingTerms.Any(t => t.Original.Equals(original, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 清空已处理文本缓存
        /// </summary>
        public void ClearProcessedCache()
        {
            lock (_processedLock)
            {
                _processedTexts.Clear();
            }
        }

        /// <summary>
        /// 获取当前术语库文件路径
        /// </summary>
        private string GetCurrentTerminologyFilePath()
        {
            var config = _configService.GetCurrentConfig();
            string fileName = string.IsNullOrEmpty(config.CurrentTerminologyFile)
                ? "default"
                : config.CurrentTerminologyFile;

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

            return Path.Combine(terminologiesFolder, $"{fileName}.csv");
        }

        /// <summary>
        /// 提取的术语数据结构
        /// </summary>
        private class ExtractedTerm
        {
            public string Original { get; set; } = "";
            public string Translation { get; set; } = "";
        }
    }
}
