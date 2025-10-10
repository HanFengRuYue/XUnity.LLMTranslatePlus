using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XUnity_LLMTranslatePlus.Utils
{
    /// <summary>
    /// 转义字符处理工具
    /// </summary>
    public class EscapeCharacterHandler
    {
        // 特殊字符模式
        private static readonly List<string> SpecialPatterns = new List<string>
        {
            @"\\n",      // 换行
            @"\\r",      // 回车
            @"\\t",      // 制表符
            @"\\\\",     // 反斜杠
            @"\\\""",    // 双引号
            @"\\\'",     // 单引号
            @"\{\\d+\}", // {0}, {1}, {2} 等占位符
            @"<[^>]+>",  // HTML/Unity 标签，如 <color=red>, <size=14>
            @"\[.*?\]",  // 方括号标记，如 [item]
        };

        /// <summary>
        /// 提取结果
        /// </summary>
        public class ExtractionResult
        {
            public string CleanedText { get; set; } = "";
            public List<SpecialCharInfo> SpecialChars { get; set; } = new List<SpecialCharInfo>();
        }

        /// <summary>
        /// 特殊字符信息
        /// </summary>
        public class SpecialCharInfo
        {
            public string Original { get; set; } = "";
            public string Placeholder { get; set; } = "";
            public int Position { get; set; }
        }

        /// <summary>
        /// 提取文本中的特殊字符和转义字符
        /// </summary>
        public static ExtractionResult ExtractSpecialChars(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new ExtractionResult { CleanedText = text };
            }

            var result = new ExtractionResult();
            var allMatches = new List<(Match match, int priority)>();
            int placeholderIndex = 0;

            // 收集所有模式的匹配项，按优先级排序
            for (int i = 0; i < SpecialPatterns.Count; i++)
            {
                var matches = Regex.Matches(text, SpecialPatterns[i]);
                foreach (Match match in matches)
                {
                    allMatches.Add((match, i));
                }
            }

            // 按位置排序，从后往前处理避免位置偏移
            allMatches.Sort((a, b) => b.match.Index.CompareTo(a.match.Index));

            // 去重：如果两个匹配项重叠，保留优先级高的（索引小的）
            var uniqueMatches = new List<(Match match, int priority)>();
            for (int i = 0; i < allMatches.Count; i++)
            {
                bool overlaps = false;
                var current = allMatches[i];
                int currentEnd = current.match.Index + current.match.Length;

                foreach (var unique in uniqueMatches)
                {
                    int uniqueEnd = unique.match.Index + unique.match.Length;
                    
                    // 检查是否重叠
                    if ((current.match.Index >= unique.match.Index && current.match.Index < uniqueEnd) ||
                        (currentEnd > unique.match.Index && currentEnd <= uniqueEnd) ||
                        (current.match.Index <= unique.match.Index && currentEnd >= uniqueEnd))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    uniqueMatches.Add(current);
                }
            }

            // 从后往前替换，使用StringBuilder提高效率
            var sb = new System.Text.StringBuilder(text);
            var specialChars = new List<SpecialCharInfo>();

            foreach (var (match, _) in uniqueMatches)
            {
                string placeholder = $"【SPECIAL_{placeholderIndex}】";
                
                specialChars.Add(new SpecialCharInfo
                {
                    Original = match.Value,
                    Placeholder = placeholder,
                    Position = match.Index
                });

                // 精确替换：在指定位置替换指定长度的文本
                sb.Remove(match.Index, match.Length);
                sb.Insert(match.Index, placeholder);
                
                placeholderIndex++;
            }

            // 由于是从后往前处理的，需要反转列表以保持原始顺序
            specialChars.Reverse();

            result.CleanedText = sb.ToString();
            result.SpecialChars = specialChars;

            return result;
        }

        /// <summary>
        /// 还原翻译文本中的特殊字符
        /// </summary>
        public static string RestoreSpecialChars(string translatedText, List<SpecialCharInfo> specialChars)
        {
            if (string.IsNullOrEmpty(translatedText) || specialChars == null || specialChars.Count == 0)
            {
                return translatedText;
            }

            string result = translatedText;

            // 按照占位符还原
            foreach (var charInfo in specialChars)
            {
                result = result.Replace(charInfo.Placeholder, charInfo.Original);
            }

            return result;
        }

        /// <summary>
        /// 智能还原特殊字符（处理占位符位置可能改变的情况）
        /// </summary>
        public static string SmartRestoreSpecialChars(string translatedText, ExtractionResult extractionResult)
        {
            if (string.IsNullOrEmpty(translatedText) || extractionResult.SpecialChars.Count == 0)
            {
                return translatedText;
            }

            string result = translatedText;

            // 先按占位符直接替换
            foreach (var charInfo in extractionResult.SpecialChars)
            {
                if (result.Contains(charInfo.Placeholder))
                {
                    result = result.Replace(charInfo.Placeholder, charInfo.Original);
                }
            }

            // 如果还有占位符没有被替换，尝试智能匹配
            foreach (var charInfo in extractionResult.SpecialChars)
            {
                // 查找可能的占位符变体（AI 可能翻译了占位符）
                var possibleVariants = new List<string>
                {
                    charInfo.Placeholder,
                    charInfo.Placeholder.Replace("【", "[").Replace("】", "]"),
                    charInfo.Placeholder.Replace("SPECIAL", "特殊"),
                };

                foreach (var variant in possibleVariants)
                {
                    if (result.Contains(variant))
                    {
                        result = result.Replace(variant, charInfo.Original);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 检查文本是否包含特殊字符
        /// </summary>
        public static bool HasSpecialChars(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (var pattern in SpecialPatterns)
            {
                if (Regex.IsMatch(text, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 统计特殊字符数量
        /// </summary>
        public static int CountSpecialChars(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int count = 0;
            foreach (var pattern in SpecialPatterns)
            {
                count += Regex.Matches(text, pattern).Count;
            }

            return count;
        }
    }
}

