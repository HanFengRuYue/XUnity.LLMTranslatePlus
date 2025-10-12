using System;
using System.IO;

namespace XUnity_LLMTranslatePlus.Utils
{
    /// <summary>
    /// 文件路径安全验证工具
    /// </summary>
    public static class PathValidator
    {
        /// <summary>
        /// 验证并规范化文件路径
        /// </summary>
        /// <param name="path">待验证的路径</param>
        /// <param name="basePath">基础路径（可选，用于验证路径边界）</param>
        /// <returns>规范化后的安全路径</returns>
        /// <exception cref="ArgumentException">路径无效时抛出</exception>
        public static string ValidateAndNormalizePath(string path, string? basePath = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("路径不能为空", nameof(path));
            }

            try
            {
                // 1. 获取完整绝对路径（防止路径遍历攻击）
                string fullPath = Path.GetFullPath(path);

                // 2. 检查路径中是否包含危险字符
                if (ContainsDangerousCharacters(fullPath))
                {
                    throw new ArgumentException($"路径包含非法字符: {path}", nameof(path));
                }

                // 3. 如果提供了基础路径，验证目标路径是否在允许的边界内
                if (!string.IsNullOrWhiteSpace(basePath))
                {
                    string fullBasePath = Path.GetFullPath(basePath);
                    if (!IsPathWithinBoundary(fullPath, fullBasePath))
                    {
                        throw new ArgumentException($"路径超出允许的范围: {path}", nameof(path));
                    }
                }

                return fullPath;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"路径验证失败: {path} - {ex.Message}", nameof(path), ex);
            }
        }

        /// <summary>
        /// 验证文件路径是否存在且可访问
        /// </summary>
        public static void ValidateFileExists(string path)
        {
            string validatedPath = ValidateAndNormalizePath(path);

            if (!File.Exists(validatedPath))
            {
                throw new FileNotFoundException($"文件不存在: {path}");
            }
        }

        /// <summary>
        /// 验证目录路径是否存在且可访问
        /// </summary>
        public static void ValidateDirectoryExists(string path)
        {
            string validatedPath = ValidateAndNormalizePath(path);

            if (!Directory.Exists(validatedPath))
            {
                throw new DirectoryNotFoundException($"目录不存在: {path}");
            }
        }

        /// <summary>
        /// 检查路径是否在指定边界内（防止路径遍历）
        /// </summary>
        private static bool IsPathWithinBoundary(string fullPath, string fullBasePath)
        {
            // 确保路径以目录分隔符结尾
            if (!fullBasePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullBasePath += Path.DirectorySeparatorChar;
            }

            // 使用不区分大小写的比较（Windows 文件系统）
            return fullPath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 检查路径是否包含危险字符
        /// </summary>
        private static bool ContainsDangerousCharacters(string path)
        {
            // 检查是否包含路径中的非法字符
            char[] invalidChars = Path.GetInvalidPathChars();
            foreach (char c in invalidChars)
            {
                if (path.Contains(c))
                {
                    return true;
                }
            }

            // 检查是否包含潜在危险的模式
            string[] dangerousPatterns = new[]
            {
                "..",      // 父目录引用
                "~",       // 用户目录引用
            };

            // 规范化后的路径不应包含这些模式
            string normalizedPath = path.Replace('/', Path.DirectorySeparatorChar);
            foreach (string pattern in dangerousPatterns)
            {
                if (normalizedPath.Contains(pattern))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 安全地创建目录（如果不存在）
        /// </summary>
        public static void EnsureDirectoryExists(string path)
        {
            string validatedPath = ValidateAndNormalizePath(path);

            if (!Directory.Exists(validatedPath))
            {
                Directory.CreateDirectory(validatedPath);
            }
        }

        /// <summary>
        /// 验证文件扩展名是否在允许列表中
        /// </summary>
        public static bool IsAllowedExtension(string path, string[] allowedExtensions)
        {
            if (allowedExtensions == null || allowedExtensions.Length == 0)
            {
                return true;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            foreach (string allowed in allowedExtensions)
            {
                if (extension == allowed.ToLowerInvariant())
                {
                    return true;
                }
            }

            return false;
        }
    }
}
