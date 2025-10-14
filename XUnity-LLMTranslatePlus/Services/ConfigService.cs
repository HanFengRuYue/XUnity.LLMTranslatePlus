using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Exceptions;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Utils;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 配置管理服务
    /// </summary>
    public class ConfigService : IDisposable
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XUnity-LLMTranslatePlus"
        );

        private static readonly string ConfigFilePath = Path.Combine(AppDataFolder, "config.json");

        private AppConfig? _currentConfig;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public ConfigService()
        {
            EnsureAppDataFolderExists();
        }

        /// <summary>
        /// 确保应用数据文件夹存在
        /// </summary>
        private void EnsureAppDataFolderExists()
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        public async Task<AppConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
        {
            // 使用 SemaphoreSlim 进行异步锁定
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                if (_currentConfig != null)
                {
                    return _currentConfig;
                }

                if (File.Exists(ConfigFilePath))
                {
                    string json = await File.ReadAllTextAsync(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);

                    if (config != null)
                    {
                        // 解密 API Key（如果已加密）
                        if (!string.IsNullOrEmpty(config.ApiKey))
                        {
                            try
                            {
                                config.ApiKey = SecureDataProtection.Unprotect(config.ApiKey);
                            }
                            catch
                            {
                                // 如果解密失败，可能是旧格式的明文，保持原样
                            }
                        }
                        _currentConfig = config;
                    }
                    else
                    {
                        _currentConfig = new AppConfig();
                    }
                }
                else
                {
                    // 首次启动，创建默认配置
                    _currentConfig = new AppConfig();
                    // 注意：这里已经持有锁，不能调用SaveConfigAsync（会死锁）
                    // 直接在这里保存文件
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    string json = JsonSerializer.Serialize(_currentConfig, options);
                    await File.WriteAllTextAsync(ConfigFilePath, json, cancellationToken);
                }
            }
            catch
            {
                // 配置文件损坏，使用默认配置
                _currentConfig = new AppConfig();
            }
            finally
            {
                _semaphore.Release();
            }

            return _currentConfig!;
        }

        /// <summary>
        /// 验证配置有效性
        /// </summary>
        private void ValidateConfig(AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.ApiUrl))
            {
                throw new ConfigurationValidationException("API URL 不能为空", nameof(config.ApiUrl));
            }

            if (!Uri.TryCreate(config.ApiUrl, UriKind.Absolute, out _))
            {
                throw new ConfigurationValidationException($"API URL 格式无效: {config.ApiUrl}", nameof(config.ApiUrl));
            }

            if (config.MaxTokens <= 0 || config.MaxTokens > 128000)
            {
                throw new ConfigurationValidationException($"最大Token数必须在1到128000之间，当前值: {config.MaxTokens}", nameof(config.MaxTokens));
            }

            if (config.Temperature < 0 || config.Temperature > 2)
            {
                throw new ConfigurationValidationException($"Temperature必须在0到2之间，当前值: {config.Temperature}", nameof(config.Temperature));
            }

            if (config.TopP < 0 || config.TopP > 1)
            {
                throw new ConfigurationValidationException($"Top P必须在0到1之间，当前值: {config.TopP}", nameof(config.TopP));
            }

            if (config.FrequencyPenalty < -2 || config.FrequencyPenalty > 2)
            {
                throw new ConfigurationValidationException($"Frequency Penalty必须在-2到2之间，当前值: {config.FrequencyPenalty}", nameof(config.FrequencyPenalty));
            }

            if (config.PresencePenalty < -2 || config.PresencePenalty > 2)
            {
                throw new ConfigurationValidationException($"Presence Penalty必须在-2到2之间，当前值: {config.PresencePenalty}", nameof(config.PresencePenalty));
            }

            if (config.Timeout <= 0 || config.Timeout > 600)
            {
                throw new ConfigurationValidationException($"超时时间必须在1到600秒之间，当前值: {config.Timeout}", nameof(config.Timeout));
            }

            if (config.RetryCount < 0 || config.RetryCount > 10)
            {
                throw new ConfigurationValidationException($"重试次数必须在0到10之间，当前值: {config.RetryCount}", nameof(config.RetryCount));
            }

            if (config.MaxConcurrentTranslations <= 0 || config.MaxConcurrentTranslations > 100)
            {
                throw new ConfigurationValidationException($"最大并发数必须在1到100之间，当前值: {config.MaxConcurrentTranslations}", nameof(config.MaxConcurrentTranslations));
            }

            if (string.IsNullOrWhiteSpace(config.SystemPrompt))
            {
                throw new ConfigurationValidationException("系统提示词不能为空", nameof(config.SystemPrompt));
            }

            if (string.IsNullOrWhiteSpace(config.TargetLanguage))
            {
                throw new ConfigurationValidationException("目标语言不能为空", nameof(config.TargetLanguage));
            }

            if (config.ErrorThreshold <= 0 || config.ErrorThreshold > 1000)
            {
                throw new ConfigurationValidationException($"错误阈值必须在1到1000之间，当前值: {config.ErrorThreshold}", nameof(config.ErrorThreshold));
            }

            if (config.ContextLines < 1)
            {
                throw new ConfigurationValidationException($"上下文行数必须大于0，当前值: {config.ContextLines}", nameof(config.ContextLines));
            }
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public async Task SaveConfigAsync(AppConfig config, CancellationToken cancellationToken = default)
        {
            // 验证配置
            ValidateConfig(config);

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                // 创建配置副本用于保存（不修改原始对象）
                var configToSave = new AppConfig
                {
                    ApiPlatform = config.ApiPlatform,
                    ApiFormat = config.ApiFormat,
                    ApiUrl = config.ApiUrl,
                    ApiKey = config.ApiKey, // 稍后加密
                    ModelName = config.ModelName,
                    MaxTokens = config.MaxTokens,
                    Temperature = config.Temperature,
                    TopP = config.TopP,
                    FrequencyPenalty = config.FrequencyPenalty,
                    PresencePenalty = config.PresencePenalty,
                    Timeout = config.Timeout,
                    RetryCount = config.RetryCount,
                    MaxConcurrentTranslations = config.MaxConcurrentTranslations,
                    SystemPrompt = config.SystemPrompt,
                    TermsDatabase = config.TermsDatabase,
                    TermsFilePath = config.TermsFilePath,
                    GameDirectory = config.GameDirectory,
                    AutoDetectPath = config.AutoDetectPath,
                    ManualTranslationFilePath = config.ManualTranslationFilePath,
                    TargetLanguage = config.TargetLanguage,
                    SourceLanguage = config.SourceLanguage,
                    PreserveSpecialChars = config.PreserveSpecialChars,
                    RealTimeMonitoring = config.RealTimeMonitoring,
                    EnableContext = config.EnableContext,
                    ContextLines = config.ContextLines,
                    EnableCache = config.EnableCache,
                    ExportLog = config.ExportLog,
                    ErrorThreshold = config.ErrorThreshold,
                    EnableAutoRefresh = config.EnableAutoRefresh,
                    AutoRefreshInterval = config.AutoRefreshInterval,
                    EnableSmartTerminology = config.EnableSmartTerminology,
                    CurrentTerminologyFile = config.CurrentTerminologyFile
                };

                // 加密 API Key
                if (!string.IsNullOrEmpty(configToSave.ApiKey))
                {
                    try
                    {
                        // 只加密未加密的 API Key
                        if (!SecureDataProtection.IsEncrypted(configToSave.ApiKey))
                        {
                            configToSave.ApiKey = SecureDataProtection.Protect(configToSave.ApiKey);
                        }
                    }
                    catch
                    {
                        // 加密失败，保存明文（向后兼容）
                    }
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string json = JsonSerializer.Serialize(configToSave, options);
                await File.WriteAllTextAsync(ConfigFilePath, json);
                _currentConfig = config; // 保存未加密的版本到内存
            }
            catch (Exception ex)
            {
                throw new Exception($"保存配置失败: {ex.Message}", ex);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 获取当前配置（同步方法）
        /// </summary>
        public AppConfig GetCurrentConfig()
        {
            // 对于只读操作，使用同步等待
            _semaphore.Wait();
            try
            {
                return _currentConfig ?? new AppConfig();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 获取应用数据文件夹路径
        /// </summary>
        public string GetAppDataFolder() => AppDataFolder;

        /// <summary>
        /// 获取配置文件路径
        /// </summary>
        public string GetConfigFilePath() => ConfigFilePath;

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }
}

