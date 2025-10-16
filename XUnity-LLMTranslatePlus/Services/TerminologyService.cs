using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Utils;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 术语库服务
    /// </summary>
    public class TerminologyService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XUnity-LLMTranslatePlus"
        );

        private static readonly string DefaultTermsFilePath = Path.Combine(AppDataFolder, "terms.csv");

        private List<Term> _terms = new List<Term>();
        private readonly object _lockObject = new object();
        private readonly LogService _logService;
        private bool _hasLogged = false;  // 标记是否已记录过加载日志

        public TerminologyService(LogService logService)
        {
            _logService = logService;
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
        /// 加载术语库
        /// </summary>
        public async Task LoadTermsAsync(string? filePath = null)
        {
            string path = filePath ?? DefaultTermsFilePath;

            // 验证文件路径
            string validatedPath = PathValidator.ValidateAndNormalizePath(path);

            try
            {
                if (File.Exists(validatedPath))
                {
                    var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        Encoding = Encoding.UTF8
                    };

                    using var reader = new StreamReader(validatedPath, Encoding.UTF8);
                    using var csv = new CsvReader(reader, config);

                    var records = csv.GetRecords<Term>().ToList();

                    lock (_lockObject)
                    {
                        _terms = records;
                    }

                    // 只在首次加载时记录日志
                    if (!_hasLogged)
                    {
                        await _logService.LogAsync($"已加载 {_terms.Count} 条术语", LogLevel.Info);
                        _hasLogged = true;
                    }
                }
                else
                {
                    // 首次加载，创建空文件
                    lock (_lockObject)
                    {
                        _terms = new List<Term>();
                    }
                    await SaveTermsAsync(validatedPath);
                    
                    // 只在首次加载时记录日志
                    if (!_hasLogged)
                    {
                        await _logService.LogAsync("术语库文件不存在，已创建新文件", LogLevel.Info);
                        _hasLogged = true;
                    }
                }
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"加载术语库失败: {ex.Message}", LogLevel.Error);
                lock (_lockObject)
                {
                    _terms = new List<Term>();
                }
            }
        }

        /// <summary>
        /// 保存术语库
        /// </summary>
        public async Task SaveTermsAsync(string? filePath = null)
        {
            string path = filePath ?? DefaultTermsFilePath;

            // 验证文件路径
            string validatedPath = PathValidator.ValidateAndNormalizePath(path);

            try
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    Encoding = Encoding.UTF8
                };

                List<Term> termsToSave;
                lock (_lockObject)
                {
                    termsToSave = _terms.ToList();
                }

                using var writer = new StreamWriter(validatedPath, false, Encoding.UTF8);
                using var csv = new CsvWriter(writer, config);

                await csv.WriteRecordsAsync(termsToSave);

                await _logService.LogAsync($"已保存 {termsToSave.Count} 条术语", LogLevel.Info);
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"保存术语库失败: {ex.Message}", LogLevel.Error);
                throw new Exception($"保存术语库失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取所有术语
        /// </summary>
        public List<Term> GetTerms()
        {
            lock (_lockObject)
            {
                return _terms.OrderByDescending(t => t.Original.Length).ToList();
            }
        }

        /// <summary>
        /// 清空所有术语
        /// </summary>
        public void ClearTerms()
        {
            lock (_lockObject)
            {
                _terms.Clear();
            }
        }

        /// <summary>
        /// 添加术语
        /// </summary>
        public void AddTerm(Term term)
        {
            lock (_lockObject)
            {
                _terms.Add(term);
            }
        }

        /// <summary>
        /// 删除术语
        /// </summary>
        public void RemoveTerm(Term term)
        {
            lock (_lockObject)
            {
                _terms.Remove(term);
            }
        }

        /// <summary>
        /// 更新术语
        /// </summary>
        public void UpdateTerm(Term oldTerm, Term newTerm)
        {
            lock (_lockObject)
            {
                int index = _terms.IndexOf(oldTerm);
                if (index >= 0)
                {
                    _terms[index] = newTerm;
                }
            }
        }

        /// <summary>
        /// 导入 CSV 文件
        /// </summary>
        public async Task<int> ImportCsvAsync(string filePath)
        {
            // 验证文件路径
            PathValidator.ValidateFileExists(filePath);
            string validatedPath = PathValidator.ValidateAndNormalizePath(filePath);

            try
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    Encoding = Encoding.UTF8
                };

                using var reader = new StreamReader(validatedPath, Encoding.UTF8);
                using var csv = new CsvReader(reader, config);

                var importedTerms = csv.GetRecords<Term>().ToList();

                lock (_lockObject)
                {
                    _terms.AddRange(importedTerms);
                }

                await _logService.LogAsync($"已导入 {importedTerms.Count} 条术语", LogLevel.Info);
                return importedTerms.Count;
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"导入术语失败: {ex.Message}", LogLevel.Error);
                throw new Exception($"导入术语失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 导出 CSV 文件
        /// </summary>
        public async Task ExportCsvAsync(string filePath)
        {
            await SaveTermsAsync(filePath);
        }

        /// <summary>
        /// 在文本中应用术语
        /// </summary>
        public string ApplyTerms(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            List<Term> enabledTerms;
            lock (_lockObject)
            {
                enabledTerms = _terms
                    .Where(t => t.Enabled)
                    .OrderByDescending(t => t.Original.Length) // 优先替换较长的术语
                    .ToList();
            }

            string result = text;
            foreach (var term in enabledTerms)
            {
                if (!string.IsNullOrEmpty(term.Original) && !string.IsNullOrEmpty(term.Translation))
                {
                    result = result.Replace(term.Original, term.Translation);
                }
            }

            return result;
        }

        /// <summary>
        /// 构建术语参考字符串
        /// </summary>
        public string BuildTermsReference(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            List<Term> enabledTerms;
            lock (_lockObject)
            {
                enabledTerms = _terms
                    .Where(t => t.Enabled && text.Contains(t.Original))
                    .OrderByDescending(t => t.Original.Length)
                    .ToList();
            }

            if (enabledTerms.Count == 0)
            {
                return "无";
            }

            var references = enabledTerms.Select(t => $"{t.Original} = {t.Translation}");
            return string.Join(", ", references);
        }

        /// <summary>
        /// 查找与文本完全匹配的术语
        /// </summary>
        /// <param name="text">要匹配的文本</param>
        /// <returns>匹配的术语，如果没有匹配则返回 null</returns>
        public Term? FindExactTerm(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            lock (_lockObject)
            {
                // 查找启用的、原文完全匹配的术语
                return _terms
                    .Where(t => t.Enabled && string.Equals(t.Original, text, StringComparison.Ordinal))
                    .OrderByDescending(t => t.Original.Length) // 优先返回较长的匹配
                    .FirstOrDefault();
            }
        }

        /// <summary>
        /// 获取默认术语文件路径
        /// </summary>
        public string GetDefaultTermsFilePath() => DefaultTermsFilePath;
    }
}

