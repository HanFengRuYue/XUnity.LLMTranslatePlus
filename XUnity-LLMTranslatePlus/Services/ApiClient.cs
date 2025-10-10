using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Models;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// AI API 客户端
    /// </summary>
    public class ApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly LogService _logService;

        public ApiClient(LogService logService)
        {
            _logService = logService;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60) // 默认超时60秒
            };
        }

        /// <summary>
        /// OpenAI API 请求模型
        /// </summary>
        private class OpenAIRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = "";

            [JsonPropertyName("messages")]
            public Message[] Messages { get; set; } = Array.Empty<Message>();

            [JsonPropertyName("max_tokens")]
            public int? MaxTokens { get; set; }

            [JsonPropertyName("temperature")]
            public double? Temperature { get; set; }

            [JsonPropertyName("top_p")]
            public double? TopP { get; set; }

            [JsonPropertyName("frequency_penalty")]
            public double? FrequencyPenalty { get; set; }

            [JsonPropertyName("presence_penalty")]
            public double? PresencePenalty { get; set; }
        }

        /// <summary>
        /// 消息模型
        /// </summary>
        private class Message
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = "";

            [JsonPropertyName("content")]
            public string Content { get; set; } = "";
        }

        /// <summary>
        /// OpenAI API 响应模型
        /// </summary>
        private class OpenAIResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("choices")]
            public Choice[] Choices { get; set; } = Array.Empty<Choice>();

            [JsonPropertyName("error")]
            public ErrorInfo? Error { get; set; }
        }

        /// <summary>
        /// 选择模型
        /// </summary>
        private class Choice
        {
            [JsonPropertyName("message")]
            public Message? Message { get; set; }

            [JsonPropertyName("finish_reason")]
            public string FinishReason { get; set; } = "";
        }

        /// <summary>
        /// 错误信息模型
        /// </summary>
        private class ErrorInfo
        {
            [JsonPropertyName("message")]
            public string Message { get; set; } = "";

            [JsonPropertyName("type")]
            public string Type { get; set; } = "";
        }

        /// <summary>
        /// 翻译文本
        /// </summary>
        public async Task<string> TranslateAsync(string text, string systemPrompt, AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            int retryCount = 0;
            Exception? lastException = null;

            while (retryCount <= config.RetryCount)
            {
                try
                {
                    // 构建请求
                    var request = new OpenAIRequest
                    {
                        Model = config.ModelName,
                        Messages = new[]
                        {
                            new Message { Role = "system", Content = systemPrompt },
                            new Message { Role = "user", Content = text }
                        },
                        MaxTokens = config.MaxTokens,
                        Temperature = config.Temperature,
                        TopP = config.TopP,
                        FrequencyPenalty = config.FrequencyPenalty,
                        PresencePenalty = config.PresencePenalty
                    };

                    var jsonOptions = new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    };

                    string requestJson = JsonSerializer.Serialize(request, jsonOptions);

                    // 发送请求
                    var httpRequest = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl)
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                    };

                    // 添加 Authorization 头
                    if (!string.IsNullOrWhiteSpace(config.ApiKey))
                    {
                        httpRequest.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
                    }

                    await _logService.LogAsync($"发送翻译请求 (尝试 {retryCount + 1}/{config.RetryCount + 1})", LogLevel.Debug);

                    // 使用CancellationToken控制超时
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.Timeout));
                    var httpResponse = await _httpClient.SendAsync(httpRequest, cts.Token);
                    string responseJson = await httpResponse.Content.ReadAsStringAsync();

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        await _logService.LogAsync($"API 请求失败: {httpResponse.StatusCode} - {responseJson}", LogLevel.Error);
                        throw new Exception($"API 请求失败: {httpResponse.StatusCode}");
                    }

                    // 解析响应
                    var response = JsonSerializer.Deserialize<OpenAIResponse>(responseJson);

                    if (response?.Error != null)
                    {
                        throw new Exception($"API 返回错误: {response.Error.Message}");
                    }

                    if (response?.Choices == null || response.Choices.Length == 0)
                    {
                        throw new Exception("API 响应中没有翻译结果");
                    }

                    string translatedText = response.Choices[0].Message?.Content?.Trim() ?? "";

                    if (string.IsNullOrWhiteSpace(translatedText))
                    {
                        throw new Exception("翻译结果为空");
                    }

                    await _logService.LogAsync($"翻译成功: {text.Substring(0, Math.Min(20, text.Length))}... -> {translatedText.Substring(0, Math.Min(20, translatedText.Length))}...", LogLevel.Debug);

                    return translatedText;
                }
                catch (OperationCanceledException)
                {
                    // TaskCanceledException也会被这里捕获，因为它是OperationCanceledException的子类
                    lastException = new Exception($"请求超时 ({config.Timeout}秒)");
                    await _logService.LogAsync($"翻译请求超时 (尝试 {retryCount + 1}/{config.RetryCount + 1})", LogLevel.Warning);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    await _logService.LogAsync($"翻译失败: {ex.Message} (尝试 {retryCount + 1}/{config.RetryCount + 1})", LogLevel.Warning);
                }

                retryCount++;

                // 如果还有重试次数，等待一段时间后重试
                if (retryCount <= config.RetryCount)
                {
                    await Task.Delay(2000 * retryCount); // 递增延迟
                }
            }

            // 所有重试都失败了
            string errorMessage = lastException?.Message ?? "未知错误";
            await _logService.LogAsync($"翻译最终失败: {errorMessage}", LogLevel.Error);
            throw new Exception($"翻译失败 (已重试 {config.RetryCount} 次): {errorMessage}", lastException);
        }

        /// <summary>
        /// 获取可用模型列表
        /// </summary>
        public async Task<List<string>> GetModelsAsync(AppConfig config)
        {
            try
            {
                await _logService.LogAsync("正在获取模型列表...", LogLevel.Info);

                // 构建模型列表API URL
                string modelsUrl = GetModelsApiUrl(config.ApiUrl);
                
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, modelsUrl);

                // 添加 Authorization 头
                if (!string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    httpRequest.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var httpResponse = await _httpClient.SendAsync(httpRequest, cts.Token);
                string responseJson = await httpResponse.Content.ReadAsStringAsync();

                if (!httpResponse.IsSuccessStatusCode)
                {
                    await _logService.LogAsync($"获取模型列表失败: {httpResponse.StatusCode}", LogLevel.Error);
                    return new List<string>();
                }

                // 解析模型列表（OpenAI格式）
                var jsonDoc = JsonDocument.Parse(responseJson);
                var models = new List<string>();

                if (jsonDoc.RootElement.TryGetProperty("data", out var dataArray))
                {
                    foreach (var model in dataArray.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var id))
                        {
                            models.Add(id.GetString() ?? "");
                        }
                    }
                }

                await _logService.LogAsync($"成功获取 {models.Count} 个模型", LogLevel.Info);
                return models.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"获取模型列表失败: {ex.Message}", LogLevel.Error);
                return new List<string>();
            }
        }

        private string GetModelsApiUrl(string chatCompletionsUrl)
        {
            // 从chat/completions URL推导出models URL
            if (chatCompletionsUrl.Contains("/v1/chat/completions"))
            {
                return chatCompletionsUrl.Replace("/v1/chat/completions", "/v1/models");
            }
            else if (chatCompletionsUrl.Contains("/chat/completions"))
            {
                return chatCompletionsUrl.Replace("/chat/completions", "/models");
            }
            
            // 默认返回OpenAI格式
            return "https://api.openai.com/v1/models";
        }

        /// <summary>
        /// 测试 API 连接
        /// </summary>
        public async Task<bool> TestConnectionAsync(AppConfig config)
        {
            try
            {
                await _logService.LogAsync("正在测试 API 连接...", LogLevel.Info);

                string testText = "Hello";
                string testPrompt = "Translate to Chinese: ";

                await TranslateAsync(testText, testPrompt, config);

                await _logService.LogAsync("API 连接测试成功", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"API 连接测试失败: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

