using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Models;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// 日志服务
    /// </summary>
    public class LogService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XUnity-LLMTranslatePlus"
        );

        private static readonly string LogFolder = Path.Combine(AppDataFolder, "logs");
        private const int MaxCachedLogs = 1000;

        private readonly ObservableCollection<LogEntry> _cachedLogs = new ObservableCollection<LogEntry>();
        private readonly object _lockObject = new object();
        private LogLevel _currentLogLevel = LogLevel.Info;

        public event EventHandler<LogEntry>? LogAdded;

        public LogService()
        {
            EnsureLogFolderExists();
        }

        /// <summary>
        /// 确保日志文件夹存在
        /// </summary>
        private void EnsureLogFolderExists()
        {
            if (!Directory.Exists(LogFolder))
            {
                Directory.CreateDirectory(LogFolder);
            }
        }

        /// <summary>
        /// 设置日志级别
        /// </summary>
        public void SetLogLevel(LogLevel level)
        {
            _currentLogLevel = level;
        }

        /// <summary>
        /// 获取当前日志级别
        /// </summary>
        public LogLevel GetLogLevel() => _currentLogLevel;

        /// <summary>
        /// 记录日志
        /// </summary>
        public async Task LogAsync(string message, LogLevel level = LogLevel.Info)
        {
            // 检查日志级别
            if (level < _currentLogLevel)
            {
                return;
            }

            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Level = level.ToString(),
                Message = message
            };

            // 添加到缓存
            lock (_lockObject)
            {
                _cachedLogs.Add(logEntry);

                // 保持缓存大小
                while (_cachedLogs.Count > MaxCachedLogs)
                {
                    _cachedLogs.RemoveAt(0);
                }
            }

            // 触发事件通知 UI
            LogAdded?.Invoke(this, logEntry);

            // 异步写入文件
            await WriteLogToFileAsync(logEntry);
        }

        /// <summary>
        /// 同步记录日志方法
        /// </summary>
        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            _ = LogAsync(message, level);
        }

        /// <summary>
        /// 写入日志到文件
        /// </summary>
        private async Task WriteLogToFileAsync(LogEntry logEntry)
        {
            try
            {
                string logFileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
                string logFilePath = Path.Combine(LogFolder, logFileName);

                string logLine = $"[{logEntry.Timestamp}] [{logEntry.Level}] {logEntry.Message}{Environment.NewLine}";

                await File.AppendAllTextAsync(logFilePath, logLine);
            }
            catch (Exception ex)
            {
                // 写入日志失败，避免递归
                Console.WriteLine($"写入日志失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取最近的日志
        /// </summary>
        public List<LogEntry> GetRecentLogs()
        {
            lock (_lockObject)
            {
                return _cachedLogs.ToList();
            }
        }

        /// <summary>
        /// 获取可观察的日志集合
        /// </summary>
        public ObservableCollection<LogEntry> GetObservableLogs()
        {
            return _cachedLogs;
        }

        /// <summary>
        /// 清空缓存的日志
        /// </summary>
        public void ClearCachedLogs()
        {
            lock (_lockObject)
            {
                _cachedLogs.Clear();
            }
        }

        /// <summary>
        /// 导出日志到文件
        /// </summary>
        public async Task<string> ExportLogsAsync(string targetPath)
        {
            try
            {
                var logs = GetRecentLogs();
                var lines = logs.Select(log => $"[{log.Timestamp}] [{log.Level}] {log.Message}");
                
                await File.WriteAllLinesAsync(targetPath, lines);
                
                return targetPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"导出日志失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取日志文件夹路径
        /// </summary>
        public string GetLogFolder() => LogFolder;
    }
}

