using System;
using System.Collections.Generic;

namespace XUnity_LLMTranslatePlus.Models
{
    /// <summary>
    /// 资产提取配置数据模型
    /// </summary>
    public class AssetExtractionConfig
    {
        /// <summary>
        /// 是否扫描 TextAsset 类型资产（通常包含对话、配置文件等文本）
        /// </summary>
        public bool ScanTextAssets { get; set; } = true;

        /// <summary>
        /// 是否扫描 MonoBehaviour 类型资产（游戏自定义脚本组件）
        /// </summary>
        public bool ScanMonoBehaviours { get; set; } = true;

        /// <summary>
        /// 是否扫描 GameObject 名称（场景对象名称，通常不需要翻译）
        /// </summary>
        public bool ScanGameObjectNames { get; set; } = false;

        /// <summary>
        /// MonoBehaviour 字段名列表（指定要提取哪些字段的文本）
        /// 常见字段名示例：text, dialogText, description, itemName, tooltipText, message, content
        /// </summary>
        public List<string> MonoBehaviourFields { get; set; } = new List<string>
        {
            "text",
            "dialogText",
            "description",
            "itemName",
            "tooltipText",
            "message",
            "content",
            "title",
            "subtitle"
        };

        /// <summary>
        /// 最小文本长度（过滤过短的文本，避免提取变量名等）
        /// </summary>
        public int MinTextLength { get; set; } = 2;

        /// <summary>
        /// 最大文本长度（过滤过长的文本，避免提取整个文件内容）
        /// </summary>
        public int MaxTextLength { get; set; } = 1000;

        /// <summary>
        /// 源语言过滤器（指定要提取哪些语言的文本）
        /// 选项：全部语言、中日韩（CJK）、简体中文、繁体中文、日语、英语、韩语、俄语
        /// </summary>
        public string SourceLanguageFilter { get; set; } = "全部语言";

        /// <summary>
        /// 排除规则列表（正则表达式模式，匹配的文本将被忽略）
        /// </summary>
        public List<string> ExcludePatterns { get; set; } = new List<string>
        {
            @"^Assets/",         // Unity 资产路径
            @"^Resources/",      // 资源路径
            @"^Prefabs/",        // 预制体路径
            @"^m_",              // Unity 内部字段名
            @"^\d+$",            // 纯数字
            @"^[a-zA-Z_][a-zA-Z0-9_]*$"  // 变量名格式（仅字母数字下划线）
        };

        /// <summary>
        /// 是否覆盖已有翻译（false = 保留已有翻译，仅翻译新文本）
        /// </summary>
        public bool OverwriteExisting { get; set; } = false;

        /// <summary>
        /// 批量翻译的并发数（与 API 端点配置的并发数相同）
        /// </summary>
        public int BatchConcurrency { get; set; } = 3;

        /// <summary>
        /// 是否在提取后显示预览（允许用户选择性翻译）
        /// </summary>
        public bool ShowPreviewBeforeTranslation { get; set; } = true;

        /// <summary>
        /// 是否递归扫描子目录
        /// </summary>
        public bool RecursiveScan { get; set; } = true;

        /// <summary>
        /// 资产文件扩展名过滤（留空表示扫描所有 Unity 资产文件）
        /// </summary>
        public List<string> FileExtensions { get; set; } = new List<string>
        {
            ".assets",
            ".unity3d",
            ".bundle"
        };

        /// <summary>
        /// ClassDatabase 文件路径（classdata.tpk）
        /// 如果为空，将优先尝试 AppData 默认路径，最后使用资产内置 TypeTree
        /// </summary>
        public string ClassDatabasePath { get; set; } = "";
    }

    /// <summary>
    /// 提取的文本条目
    /// </summary>
    public class ExtractedTextEntry
    {
        /// <summary>
        /// 原始文本
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// 来源资产文件路径
        /// </summary>
        public string SourceAsset { get; set; } = "";

        /// <summary>
        /// 来源资产类型（TextAsset, MonoBehaviour, GameObject 等）
        /// </summary>
        public string AssetType { get; set; } = "";

        /// <summary>
        /// 字段名（对于 MonoBehaviour）
        /// </summary>
        public string FieldName { get; set; } = "";

        /// <summary>
        /// 是否已选择翻译
        /// </summary>
        public bool IsSelected { get; set; } = true;

        /// <summary>
        /// 字段名透明度（用于UI显示，仅 MonoBehaviour 显示字段名）
        /// </summary>
        public double FieldNameOpacity => string.IsNullOrEmpty(FieldName) ? 0.3 : 1.0;
    }

    /// <summary>
    /// 资产扫描进度信息
    /// </summary>
    public class AssetScanProgress
    {
        /// <summary>
        /// 总资产文件数
        /// </summary>
        public int TotalAssets { get; set; }

        /// <summary>
        /// 已处理资产文件数
        /// </summary>
        public int ProcessedAssets { get; set; }

        /// <summary>
        /// 已提取文本数
        /// </summary>
        public int ExtractedTexts { get; set; }

        /// <summary>
        /// 当前处理的资产文件名
        /// </summary>
        public string CurrentAsset { get; set; } = "";

        /// <summary>
        /// 进度百分比
        /// </summary>
        public double ProgressPercentage => TotalAssets > 0
            ? (double)ProcessedAssets / TotalAssets * 100
            : 0;

        /// <summary>
        /// 是否已取消
        /// </summary>
        public bool IsCancelled { get; set; }
    }
}
