using System;

namespace XUnity_LLMTranslatePlus.Exceptions
{
    /// <summary>
    /// 翻译相关异常基类
    /// </summary>
    public class TranslationException : Exception
    {
        public TranslationException(string message) : base(message)
        {
        }

        public TranslationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// API 连接异常
    /// </summary>
    public class ApiConnectionException : TranslationException
    {
        public string? ApiUrl { get; }
        public int? StatusCode { get; }

        public ApiConnectionException(string message, string apiUrl, int? statusCode = null)
            : base(message)
        {
            ApiUrl = apiUrl;
            StatusCode = statusCode;
        }

        public ApiConnectionException(string message, string apiUrl, Exception innerException, int? statusCode = null)
            : base(message, innerException)
        {
            ApiUrl = apiUrl;
            StatusCode = statusCode;
        }

        public override string ToString()
        {
            return $"{base.ToString()}\nAPI URL: {ApiUrl}\nStatus Code: {StatusCode}";
        }
    }

    /// <summary>
    /// API 响应异常
    /// </summary>
    public class ApiResponseException : TranslationException
    {
        public string? ResponseContent { get; }

        public ApiResponseException(string message, string? responseContent = null)
            : base(message)
        {
            ResponseContent = responseContent;
        }

        public ApiResponseException(string message, Exception innerException, string? responseContent = null)
            : base(message, innerException)
        {
            ResponseContent = responseContent;
        }

        public override string ToString()
        {
            string result = base.ToString();
            if (!string.IsNullOrWhiteSpace(ResponseContent))
            {
                result += $"\nResponse: {ResponseContent}";
            }
            return result;
        }
    }

    /// <summary>
    /// 翻译超时异常
    /// </summary>
    public class TranslationTimeoutException : TranslationException
    {
        public int TimeoutSeconds { get; }
        public int RetryAttempt { get; }

        public TranslationTimeoutException(string message, int timeoutSeconds, int retryAttempt = 0)
            : base(message)
        {
            TimeoutSeconds = timeoutSeconds;
            RetryAttempt = retryAttempt;
        }

        public override string ToString()
        {
            return $"{base.ToString()}\nTimeout: {TimeoutSeconds}s\nRetry Attempt: {RetryAttempt}";
        }
    }

    /// <summary>
    /// API速率限制异常（HTTP 429 Too Many Requests）
    /// </summary>
    public class RateLimitException : ApiConnectionException
    {
        public int? RetryAfterSeconds { get; }

        public RateLimitException(string message, string apiUrl, int? retryAfterSeconds = null)
            : base(message, apiUrl, 429)
        {
            RetryAfterSeconds = retryAfterSeconds;
        }

        public override string ToString()
        {
            string result = base.ToString();
            if (RetryAfterSeconds.HasValue)
            {
                result += $"\nRetry After: {RetryAfterSeconds}s";
            }
            return result;
        }
    }

    /// <summary>
    /// 配置验证异常
    /// </summary>
    public class ConfigurationValidationException : Exception
    {
        public string? ConfigField { get; }

        public ConfigurationValidationException(string message, string? configField = null)
            : base(message)
        {
            ConfigField = configField;
        }

        public override string ToString()
        {
            string result = base.ToString();
            if (!string.IsNullOrWhiteSpace(ConfigField))
            {
                result += $"\nConfiguration Field: {ConfigField}";
            }
            return result;
        }
    }

    /// <summary>
    /// 文件操作异常
    /// </summary>
    public class FileOperationException : Exception
    {
        public string? FilePath { get; }
        public string? Operation { get; }

        public FileOperationException(string message, string? filePath = null, string? operation = null)
            : base(message)
        {
            FilePath = filePath;
            Operation = operation;
        }

        public FileOperationException(string message, Exception innerException, string? filePath = null, string? operation = null)
            : base(message, innerException)
        {
            FilePath = filePath;
            Operation = operation;
        }

        public override string ToString()
        {
            string result = base.ToString();
            if (!string.IsNullOrWhiteSpace(FilePath))
            {
                result += $"\nFile Path: {FilePath}";
            }
            if (!string.IsNullOrWhiteSpace(Operation))
            {
                result += $"\nOperation: {Operation}";
            }
            return result;
        }
    }
}
