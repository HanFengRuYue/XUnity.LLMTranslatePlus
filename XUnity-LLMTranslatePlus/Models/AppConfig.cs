using System.Collections.Generic;

namespace XUnity_LLMTranslatePlus.Models
{
    /// <summary>
    /// 应用程序配置数据模型
    /// </summary>
    public class AppConfig
    {
        // API 基础配置
        public string ApiPlatform { get; set; } = "OpenAI";
        public string ApiFormat { get; set; } = "OpenAI"; // API请求格式：OpenAI, Claude, Gemini等
        public string ApiUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
        public string ApiKey { get; set; } = "";
        public string ModelName { get; set; } = "gpt-3.5-turbo";

        // API 高级配置
        public int MaxTokens { get; set; } = 4096;
        public double Temperature { get; set; } = 0.7;
        public double TopP { get; set; } = 1.0;
        public double FrequencyPenalty { get; set; } = 0.0;
        public double PresencePenalty { get; set; } = 0.0;
        public int Timeout { get; set; } = 30;
        public int RetryCount { get; set; } = 3;

        // 并发配置
        public int MaxConcurrentTranslations { get; set; } = 3;

        // 系统提示词
        public string SystemPrompt { get; set; } = "你是一个专业的游戏文本翻译助手。请将以下文本翻译成{目标语言}。保持原文的语气和风格，确保翻译准确、流畅、自然。\n\n【重要】如果文本中包含形如【SPECIAL_数字】的占位符，请务必在译文中完整保留这些占位符，不要翻译或修改它们。\n\n原文：{原文}\n\n术语参考：{术语}\n\n上下文参考：{上下文}\n\n请只输出翻译结果，不要包含任何解释或额外内容。";

        // 术语库
        public List<Term> TermsDatabase { get; set; } = new List<Term>();
        public string TermsFilePath { get; set; } = "";

        // 翻译设置
        public string GameDirectory { get; set; } = "";
        public bool AutoDetectPath { get; set; } = true;
        public string ManualTranslationFilePath { get; set; } = ""; // 手动指定的翻译文件路径
        public string TargetLanguage { get; set; } = "简体中文";
        public string SourceLanguage { get; set; } = "自动检测";
        public bool PreserveSpecialChars { get; set; } = true;
        public bool RealTimeMonitoring { get; set; } = true;

        // 上下文配置
        public bool EnableContext { get; set; } = false;
        public int ContextLines { get; set; } = 3;
        public double ContextWeight { get; set; } = 0.5;

        // 输出配置
        public bool EnableCache { get; set; } = true;
        public bool ExportLog { get; set; } = false;

        // 错误控制
        public int ErrorThreshold { get; set; } = 100; // 错误阈值，默认100个错误后停止
    }

    /// <summary>
    /// 术语条目
    /// </summary>
    public class Term
    {
        public string Original { get; set; } = "";
        public string Translation { get; set; } = "";
        public int Priority { get; set; } = 1;
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// 翻译条目
    /// </summary>
    public class TranslationEntry
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public bool IsTranslated { get; set; } = false;
        public bool HasError { get; set; } = false;
        public string SpecialChars { get; set; } = "";
    }

    /// <summary>
    /// 日志条目
    /// </summary>
    public class LogEntry
    {
        public string Timestamp { get; set; } = "";
        public string Level { get; set; } = "Info";
        public string Message { get; set; } = "";
    }
}

