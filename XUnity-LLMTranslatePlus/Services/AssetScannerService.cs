using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XUnity_LLMTranslatePlus.Models;
using XUnity_LLMTranslatePlus.Utils;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 资产文件扫描服务
    /// 负责扫描游戏目录，查找 Unity 资产文件
    /// </summary>
    public class AssetScannerService
    {
        private readonly LogService _logService;

        public AssetScannerService(LogService logService)
        {
            _logService = logService;
        }

        /// <summary>
        /// 扫描游戏目录，查找所有 Unity 资产文件
        /// </summary>
        /// <param name="gameDirectory">游戏根目录</param>
        /// <param name="config">提取配置</param>
        /// <param name="progress">进度回调</param>
        /// <param name="cancellationToken">取消标记</param>
        /// <returns>资产文件路径列表</returns>
        public async Task<List<string>> ScanAssetsAsync(
            string gameDirectory,
            AssetExtractionConfig config,
            IProgress<AssetScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 验证目录路径
                PathValidator.ValidateDirectoryExists(gameDirectory);
                string validatedGameDir = PathValidator.ValidateAndNormalizePath(gameDirectory);

                await _logService.LogAsync($"开始扫描游戏目录: {validatedGameDir}", LogLevel.Info);

                var candidateFiles = new List<string>();

                // 搜索选项
                var searchOption = config.RecursiveScan
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                // 明显的非资产文件扩展名（排除列表）
                var excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".dll", ".exe", ".so", ".dylib",      // 二进制库/可执行文件
                    ".txt", ".xml", ".json", ".csv",      // 文本配置文件
                    ".pdb", ".mdb",                        // 调试符号
                    ".log", ".ini", ".cfg", ".config",    // 日志和配置
                    ".manifest", ".meta",                  // Unity 元数据
                    ".md", ".pdf", ".doc", ".docx",       // 文档
                    ".mp3", ".wav", ".ogg", ".m4a",       // 音频（通常不是资产文件）
                    ".mp4", ".avi", ".mov", ".mkv",       // 视频
                    ".jpg", ".jpeg", ".png", ".bmp", ".tga", ".gif" // 图像（通常不直接是资产文件）
                };

                // 遍历所有文件
                try
                {
                    var allFiles = Directory.GetFiles(validatedGameDir, "*", searchOption);

                    await _logService.LogAsync(
                        $"找到 {allFiles.Length} 个文件，开始筛选...",
                        LogLevel.Info);

                    foreach (var file in allFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var extension = Path.GetExtension(file);

                        // 跳过明显的非资产文件
                        if (!string.IsNullOrEmpty(extension) && excludedExtensions.Contains(extension))
                        {
                            continue;
                        }

                        // 所有其他文件（包括无扩展名文件）都作为候选
                        candidateFiles.Add(file);
                    }

                    await _logService.LogAsync(
                        $"筛选完成，共 {candidateFiles.Count} 个候选文件（已排除 {allFiles.Length - candidateFiles.Count} 个明显的非资产文件）",
                        LogLevel.Info);
                }
                catch (UnauthorizedAccessException ex)
                {
                    await _logService.LogAsync(
                        $"访问目录被拒绝: {ex.Message}",
                        LogLevel.Warning);
                }
                catch (Exception ex)
                {
                    await _logService.LogAsync(
                        $"扫描文件时出错: {ex.Message}",
                        LogLevel.Error);
                    throw;
                }

                await _logService.LogAsync(
                    $"扫描完成，共找到 {candidateFiles.Count} 个候选资产文件",
                    LogLevel.Info);

                // 报告最终进度
                progress?.Report(new AssetScanProgress
                {
                    TotalAssets = candidateFiles.Count,
                    ProcessedAssets = 0,
                    ExtractedTexts = 0,
                    CurrentAsset = "扫描完成"
                });

                return candidateFiles;
            }
            catch (OperationCanceledException)
            {
                await _logService.LogAsync("资产扫描已取消", LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"资产扫描失败: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// 查找特定目录下的资产文件（用于快速定位常见位置）
        /// </summary>
        /// <param name="gameDirectory">游戏根目录</param>
        /// <returns>推荐的资产文件路径列表</returns>
        public async Task<List<string>> FindCommonAssetLocationsAsync(string gameDirectory)
        {
            var commonPaths = new List<string>
            {
                // Unity 标准构建位置
                Path.Combine(gameDirectory, "Data"),
                Path.Combine(gameDirectory, $"{Path.GetFileName(gameDirectory)}_Data"),

                // 常见资产目录
                Path.Combine(gameDirectory, "StreamingAssets"),
                Path.Combine(gameDirectory, "AssetBundles"),
                Path.Combine(gameDirectory, "Resources"),

                // Android APK 提取位置（如果用户从 APK 提取了资产）
                Path.Combine(gameDirectory, "assets"),
                Path.Combine(gameDirectory, "assets", "bin", "Data")
            };

            var existingPaths = new List<string>();

            foreach (var path in commonPaths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        existingPaths.Add(path);
                        await _logService.LogAsync($"发现常见资产目录: {path}", LogLevel.Debug);
                    }
                }
                catch (Exception ex)
                {
                    await _logService.LogAsync(
                        $"检查目录失败 {path}: {ex.Message}",
                        LogLevel.Debug);
                }
            }

            return existingPaths;
        }

        /// <summary>
        /// 获取资产文件的基本信息（不解析内容）
        /// </summary>
        /// <param name="assetFilePath">资产文件路径</param>
        /// <returns>文件信息字典</returns>
        public Dictionary<string, string> GetAssetFileInfo(string assetFilePath)
        {
            try
            {
                var fileInfo = new FileInfo(assetFilePath);

                return new Dictionary<string, string>
                {
                    ["FileName"] = fileInfo.Name,
                    ["Extension"] = fileInfo.Extension,
                    ["Size"] = FormatFileSize(fileInfo.Length),
                    ["SizeBytes"] = fileInfo.Length.ToString(),
                    ["LastModified"] = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["FullPath"] = fileInfo.FullName
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, string>
                {
                    ["Error"] = ex.Message
                };
            }
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}
