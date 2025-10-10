using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Models;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 配置管理服务
    /// </summary>
    public class ConfigService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XUnity-LLMTranslatePlus"
        );

        private static readonly string ConfigFilePath = Path.Combine(AppDataFolder, "config.json");

        private AppConfig? _currentConfig;
        private readonly object _lockObject = new object();

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
        public async Task<AppConfig> LoadConfigAsync()
        {
            lock (_lockObject)
            {
                if (_currentConfig != null)
                {
                    return _currentConfig;
                }
            }

            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = await File.ReadAllTextAsync(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    
                    lock (_lockObject)
                    {
                        _currentConfig = config ?? new AppConfig();
                    }
                }
                else
                {
                    // 首次启动，创建默认配置
                    lock (_lockObject)
                    {
                        _currentConfig = new AppConfig();
                    }
                    await SaveConfigAsync(_currentConfig);
                }
            }
            catch (Exception ex)
            {
                // 配置文件损坏，使用默认配置
                Console.WriteLine($"加载配置失败: {ex.Message}");
                lock (_lockObject)
                {
                    _currentConfig = new AppConfig();
                }
            }

            return _currentConfig!;
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public async Task SaveConfigAsync(AppConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string json = JsonSerializer.Serialize(config, options);
                await File.WriteAllTextAsync(ConfigFilePath, json);

                lock (_lockObject)
                {
                    _currentConfig = config;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"保存配置失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取当前配置（同步方法）
        /// </summary>
        public AppConfig GetCurrentConfig()
        {
            lock (_lockObject)
            {
                return _currentConfig ?? new AppConfig();
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
    }
}

