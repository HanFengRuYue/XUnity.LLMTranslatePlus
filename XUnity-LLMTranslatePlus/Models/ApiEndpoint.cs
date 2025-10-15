using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace XUnity_LLMTranslatePlus.Models
{
    /// <summary>
    /// API 端点配置
    /// </summary>
    public class ApiEndpoint : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        /// <summary>
        /// 唯一标识符
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 端点名称（用户自定义）
        /// </summary>
        public string Name { get; set; } = "未命名API";

        /// <summary>
        /// API 平台（OpenAI, DeepSeek, Claude等）
        /// </summary>
        public string Platform { get; set; } = "OpenAI";

        /// <summary>
        /// API 请求格式（OpenAI, Claude, Gemini等）
        /// </summary>
        public string ApiFormat { get; set; } = "OpenAI";

        /// <summary>
        /// API 地址
        /// </summary>
        public string Url { get; set; } = "";

        /// <summary>
        /// API 密钥
        /// </summary>
        public string ApiKey { get; set; } = "";

        /// <summary>
        /// 模型名称
        /// </summary>
        public string ModelName { get; set; } = "gpt-3.5-turbo";

        // API 高级参数
        /// <summary>
        /// 最大令牌数
        /// </summary>
        public int MaxTokens { get; set; } = 4096;

        /// <summary>
        /// 温度
        /// </summary>
        public double Temperature { get; set; } = 0.7;

        /// <summary>
        /// Top P
        /// </summary>
        public double TopP { get; set; } = 1.0;

        /// <summary>
        /// 频率惩罚
        /// </summary>
        public double FrequencyPenalty { get; set; } = 0.0;

        /// <summary>
        /// 存在惩罚
        /// </summary>
        public double PresencePenalty { get; set; } = 0.0;

        /// <summary>
        /// 超时时间（秒）
        /// </summary>
        public int Timeout { get; set; } = 30;

        /// <summary>
        /// 单个端点的最大并发请求数
        /// </summary>
        public int MaxConcurrent { get; set; } = 3;

        private bool _isEnabled = true;
        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 手动权重（可选，0-100，用于手动调整负载分配）
        /// </summary>
        public int Weight { get; set; } = 50;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 克隆端点配置
        /// </summary>
        public ApiEndpoint Clone()
        {
            return new ApiEndpoint
            {
                Id = this.Id,
                Name = this.Name,
                Platform = this.Platform,
                ApiFormat = this.ApiFormat,
                Url = this.Url,
                ApiKey = this.ApiKey,
                ModelName = this.ModelName,
                MaxTokens = this.MaxTokens,
                Temperature = this.Temperature,
                TopP = this.TopP,
                FrequencyPenalty = this.FrequencyPenalty,
                PresencePenalty = this.PresencePenalty,
                Timeout = this.Timeout,
                MaxConcurrent = this.MaxConcurrent,
                IsEnabled = this._isEnabled,  // Use backing field
                Weight = this.Weight,
                CreatedAt = this.CreatedAt
            };
        }
    }
}
