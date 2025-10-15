using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Utils;

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
    public class LogService : IDisposable
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XUnity-LLMTranslatePlus"
        );

        private static readonly string LogFolder = Path.Combine(AppDataFolder, "logs");
        private const int MaxCachedLogs = 1000;
        private const int BatchSize = 50; // 批量写入大小
        private const int FlushIntervalMs = 2000; // 2秒刷新间隔

        private readonly ObservableCollection<LogEntry> _cachedLogs = new ObservableCollection<LogEntry>();
        private readonly object _lockObject = new object();
        private LogLevel _currentLogLevel = LogLevel.Info;

        // 后台日志队列
        private readonly Channel<LogEntry> _logChannel;
        private readonly CancellationTokenSource _cts;
        private readonly Task _backgroundTask;

        public event EventHandler<LogEntry>? LogAdded;

        public LogService()
        {
            EnsureLogFolderExists();

            // 创建无界通道用于日志队列
            _logChannel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _cts = new CancellationTokenSource();
            _backgroundTask = Task.Run(() => ProcessLogQueueAsync(_cts.Token));
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

            // 将日志添加到队列（非阻塞）
            await _logChannel.Writer.WriteAsync(logEntry);
        }

        /// <summary>
        /// 同步记录日志方法
        /// </summary>
        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            _ = LogAsync(message, level);
        }

        /// <summary>
        /// 后台处理日志队列
        /// </summary>
        private async Task ProcessLogQueueAsync(CancellationToken cancellationToken)
        {
            var batch = new List<LogEntry>();
            var lastFlushTime = DateTime.Now;

            try
            {
                await foreach (var logEntry in _logChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    batch.Add(logEntry);

                    // 达到批量大小或超过刷新间隔时写入
                    var timeSinceLastFlush = (DateTime.Now - lastFlushTime).TotalMilliseconds;
                    if (batch.Count >= BatchSize || timeSinceLastFlush >= FlushIntervalMs)
                    {
                        await FlushBatchAsync(batch);
                        batch.Clear();
                        lastFlushTime = DateTime.Now;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，刷新剩余日志
                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理日志队列失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 批量刷新日志到文件
        /// </summary>
        private async Task FlushBatchAsync(List<LogEntry> batch)
        {
            if (batch.Count == 0) return;

            try
            {
                string logFileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
                string logFilePath = Path.Combine(LogFolder, logFileName);

                // 使用 StringBuilder 构建批量日志
                var sb = new StringBuilder(batch.Count * 100); // 预估每条日志100字符
                foreach (var logEntry in batch)
                {
                    sb.AppendLine($"[{logEntry.Timestamp}] [{logEntry.Level}] {logEntry.Message}");
                }

                // 一次性写入所有日志
                await File.AppendAllTextAsync(logFilePath, sb.ToString());
            }
            catch (Exception ex)
            {
                // 写入日志失败，避免递归
                Console.WriteLine($"批量写入日志失败: {ex.Message}");
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
            // 验证目标文件路径
            string validatedPath = PathValidator.ValidateAndNormalizePath(targetPath);

            try
            {
                var logs = GetRecentLogs();
                var lines = logs.Select(log => $"[{log.Timestamp}] [{log.Level}] {log.Message}");

                await File.WriteAllLinesAsync(validatedPath, lines);

                return validatedPath;
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

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            try
            {
                // 停止接收新日志
                _logChannel.Writer.Complete();

                // 请求取消后台任务
                _cts.Cancel();

                // 等待后台任务完成（最多等待5秒）
                _backgroundTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"释放日志服务失败: {ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
            }
        }
    }
}

