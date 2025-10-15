using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Exceptions;
using XUnity_LLMTranslatePlus.Models;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// AI API 客户端
    /// </summary>
    public class ApiClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly LogService _logService;

        public ApiClient(IHttpClientFactory httpClientFactory, LogService logService)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
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
        /// 翻译文本（使用 ApiEndpoint 配置）
        /// </summary>
        public async Task<string> TranslateAsync(string text, string systemPrompt, ApiEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            return await TranslateInternalAsync(
                text, systemPrompt,
                endpoint.Url, endpoint.ApiKey, endpoint.ModelName,
                endpoint.MaxTokens, endpoint.Temperature, endpoint.TopP,
                endpoint.FrequencyPenalty, endpoint.PresencePenalty,
                endpoint.Timeout, 3, // 使用固定重试次数3次
                cancellationToken);
        }

        /// <summary>
        /// 内部翻译实现（统一逻辑）
        /// </summary>
        private async Task<string> TranslateInternalAsync(
            string text, string systemPrompt,
            string apiUrl, string apiKey, string modelName,
            int maxTokens, double temperature, double topP,
            double frequencyPenalty, double presencePenalty,
            int timeout, int retryCount,
            CancellationToken cancellationToken)
        {
            int currentRetry = 0;
            Exception? lastException = null;

            while (currentRetry <= retryCount)
            {
                // 检查外部取消请求
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // 构建请求
                    var request = new OpenAIRequest
                    {
                        Model = modelName,
                        Messages = new[]
                        {
                            new Message { Role = "system", Content = systemPrompt },
                            new Message { Role = "user", Content = text }
                        },
                        MaxTokens = maxTokens,
                        Temperature = temperature,
                        TopP = topP,
                        FrequencyPenalty = frequencyPenalty,
                        PresencePenalty = presencePenalty
                    };

                    var jsonOptions = new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    };

                    string requestJson = JsonSerializer.Serialize(request, jsonOptions);

                    // 发送请求
                    var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                    };

                    // 添加 Authorization 头
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                    }

                    await _logService.LogAsync($"发送翻译请求 (尝试 {currentRetry + 1}/{retryCount + 1})", LogLevel.Debug);

                    // 创建链接的取消令牌：结合外部取消和超时取消
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    using var httpClient = _httpClientFactory.CreateClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(timeout + 5); // 额外5秒以避免双重超时
                    var httpResponse = await httpClient.SendAsync(httpRequest, linkedCts.Token);
                    string responseJson = await httpResponse.Content.ReadAsStringAsync();

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        await _logService.LogAsync($"API 请求失败: {httpResponse.StatusCode} - {responseJson}", LogLevel.Error);
                        throw new ApiConnectionException(
                            $"API 请求失败: {httpResponse.StatusCode}",
                            apiUrl,
                            (int)httpResponse.StatusCode);
                    }

                    // 解析响应
                    var response = JsonSerializer.Deserialize<OpenAIResponse>(responseJson);

                    if (response?.Error != null)
                    {
                        throw new ApiResponseException(
                            $"API 返回错误: {response.Error.Message}",
                            responseJson);
                    }

                    if (response?.Choices == null || response.Choices.Length == 0)
                    {
                        throw new ApiResponseException(
                            "API 响应中没有翻译结果",
                            responseJson);
                    }

                    string translatedText = response.Choices[0].Message?.Content?.Trim() ?? "";

                    if (string.IsNullOrWhiteSpace(translatedText))
                    {
                        throw new ApiResponseException(
                            "翻译结果为空",
                            responseJson);
                    }

                    await _logService.LogAsync($"翻译成功: {GetPreview(text, 20)}... -> {GetPreview(translatedText, 20)}...", LogLevel.Debug);

                    return translatedText;
                }
                catch (OperationCanceledException)
                {
                    // TaskCanceledException也会被这里捕获，因为它是OperationCanceledException的子类
                    lastException = new TranslationTimeoutException(
                        $"请求超时 ({timeout}秒)",
                        timeout,
                        currentRetry);
                    await _logService.LogAsync($"翻译请求超时 (尝试 {currentRetry + 1}/{retryCount + 1})", LogLevel.Warning);
                }
                catch (HttpRequestException ex)
                {
                    // 网络连接错误
                    lastException = new ApiConnectionException(
                        $"网络连接失败: {ex.Message}",
                        apiUrl,
                        ex);
                    await _logService.LogAsync($"网络连接失败: {ex.Message} (尝试 {currentRetry + 1}/{retryCount + 1})", LogLevel.Warning);
                }
                catch (JsonException ex)
                {
                    // JSON 解析错误
                    lastException = new ApiResponseException(
                        $"API 响应解析失败: {ex.Message}",
                        ex);
                    await _logService.LogAsync($"API 响应解析失败: {ex.Message} (尝试 {currentRetry + 1}/{retryCount + 1})", LogLevel.Warning);
                }
                catch (TranslationException)
                {
                    // 重新抛出自定义翻译异常
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    await _logService.LogAsync($"翻译失败: {ex.Message} (尝试 {currentRetry + 1}/{retryCount + 1})", LogLevel.Warning);
                }

                currentRetry++;

                // 如果还有重试次数，等待一段时间后重试
                if (currentRetry <= retryCount)
                {
                    await Task.Delay(2000 * currentRetry, cancellationToken); // 递增延迟，支持取消
                }
            }

            // 所有重试都失败了
            string errorMessage = lastException?.Message ?? "未知错误";
            await _logService.LogAsync($"翻译最终失败: {errorMessage}", LogLevel.Error);

            // 根据最后一个异常类型抛出相应的异常
            if (lastException is TranslationException translationEx)
            {
                throw translationEx;
            }

            // 如果lastException为null，抛出不带内部异常的TranslationException
            if (lastException == null)
            {
                throw new TranslationException($"翻译失败 (已重试 {retryCount} 次): {errorMessage}");
            }

            throw new TranslationException(
                $"翻译失败 (已重试 {retryCount} 次): {errorMessage}",
                lastException);
        }

        /// <summary>
        /// 获取可用模型列表（使用 ApiEndpoint 配置）
        /// </summary>
        public async Task<List<string>> GetModelsAsync(ApiEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            return await GetModelsInternalAsync(endpoint.Url, endpoint.ApiKey, cancellationToken);
        }

        /// <summary>
        /// 内部获取模型列表实现（统一逻辑）
        /// </summary>
        private async Task<List<string>> GetModelsInternalAsync(string apiUrl, string apiKey, CancellationToken cancellationToken)
        {
            try
            {
                await _logService.LogAsync("正在获取模型列表...", LogLevel.Info);

                // 构建模型列表API URL
                string modelsUrl = GetModelsApiUrl(apiUrl);

                var httpRequest = new HttpRequestMessage(HttpMethod.Get, modelsUrl);

                // 添加 Authorization 头
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                using var httpClient = _httpClientFactory.CreateClient();
                var httpResponse = await httpClient.SendAsync(httpRequest, cts.Token);
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
        /// 获取文本预览（使用 Span 优化）
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

