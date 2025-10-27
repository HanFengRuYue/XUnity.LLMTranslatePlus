using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AssetsTools.NET;
using AssetsTools.NET.Cpp2IL;
using AssetsTools.NET.Extra;
using XUnity_LLMTranslatePlus.Models;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 资产文本提取器
    /// 使用 AssetsTools.NET 从 Unity 资产文件中提取文本
    /// </summary>
    public class AssetTextExtractor : IDisposable
    {
        private readonly LogService _logService;
        private readonly AssetExtractionConfig _config;
        private AssetsManager? _assetsManager;
        private bool _isInitialized = false;
        private bool _monoTempGeneratorSet = false;
        private string? _gameDirectory;

        public AssetTextExtractor(LogService logService, AssetExtractionConfig config)
        {
            _logService = logService;
            _config = config;
        }

        /// <summary>
        /// 初始化 AssetsManager 和 ClassDatabase
        /// </summary>
        private async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            try
            {
                _assetsManager = new AssetsManager();

                // 启用性能优化（参考 UABEANext）
                _assetsManager.UseTemplateFieldCache = true;
                _assetsManager.UseQuickLookup = true;

                // 加载 ClassPackage（类型数据库包）
                // 简化加载逻辑：仅保留可靠的两种方式
                // 优先级：1. 程序内嵌资源 -> 2. 用户配置路径
                string? classdataPath = null;
                bool loadedFromEmbedded = false;

                // 1. 优先：从程序内嵌资源加载（最可靠）
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var resourceName = "XUnity_LLMTranslatePlus.Resources.classdata.tpk";

                    using (var stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream != null)
                        {
                            // 提取到临时文件
                            var tempPath = Path.Combine(Path.GetTempPath(), "XUnity_LLMTranslatePlus_classdata.tpk");
                            using (var fileStream = File.Create(tempPath))
                            {
                                await stream.CopyToAsync(fileStream);
                            }

                            classdataPath = tempPath;
                            loadedFromEmbedded = true;
                            await _logService.LogAsync(
                                "已从程序内嵌资源加载 ClassDatabase",
                                LogLevel.Info);
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _logService.LogAsync(
                        $"从内嵌资源加载 ClassDatabase 失败: {ex.Message}",
                        LogLevel.Warning);
                }

                // 2. 备用：用户配置的路径（允许用户自定义）
                if (!loadedFromEmbedded &&
                    !string.IsNullOrWhiteSpace(_config.ClassDatabasePath) &&
                    File.Exists(_config.ClassDatabasePath))
                {
                    classdataPath = _config.ClassDatabasePath;
                    await _logService.LogAsync(
                        $"已从用户配置路径加载 ClassDatabase: {Path.GetFileName(classdataPath)}",
                        LogLevel.Info);
                }

                // 加载 ClassPackage（包含多个Unity版本的类型定义）
                if (classdataPath != null)
                {
                    try
                    {
                        _assetsManager.LoadClassPackage(classdataPath);
                        await _logService.LogAsync("ClassPackage 加载成功", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        throw new FileLoadException(
                            $"ClassDatabase 加载失败: {ex.Message}。请检查程序完整性或手动指定有效的 classdata.tpk 文件。",
                            ex);
                    }
                }
                else
                {
                    throw new FileNotFoundException(
                        "无法加载 ClassDatabase。程序内嵌资源未找到，且未配置用户路径。请检查程序完整性或手动指定 classdata.tpk 文件。");
                }

                // 尝试设置 MonoTempGenerator（用于没有 TypeTree 的 MonoBehaviour）
                await SetupMonoTempGeneratorAsync();

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                await _logService.LogAsync($"初始化 AssetsManager 失败: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// 从资产文件列表中提取所有文本
        /// </summary>
        public async Task<List<ExtractedTextEntry>> ExtractTextsAsync(
            string gameDirectory,
            List<string> assetFilePaths,
            IProgress<AssetScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _gameDirectory = gameDirectory;
            await InitializeAsync();

            var allExtractedTexts = new List<ExtractedTextEntry>();
            int processedCount = 0;
            int successCount = 0;  // 成功加载的资产文件数
            int skippedCount = 0;  // 跳过的非资产文件数

            foreach (var assetPath in assetFilePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    int textCountBefore = allExtractedTexts.Count;
                    var texts = await ExtractFromSingleAssetAsync(assetPath, cancellationToken);
                    allExtractedTexts.AddRange(texts);

                    processedCount++;

                    // 判断是否成功加载资产文件
                    if (texts.Count > 0)
                    {
                        successCount++;
                        await _logService.LogAsync(
                            $"已处理 {Path.GetFileName(assetPath)}: 提取 {texts.Count} 条文本",
                            LogLevel.Info);
                    }
                    else
                    {
                        // 加载成功但没有提取到文本（可能是空资产或不包含目标类型）
                        // 不增加 skippedCount，因为这是有效的资产文件
                        await _logService.LogAsync(
                            $"已处理 {Path.GetFileName(assetPath)}: 未找到文本",
                            LogLevel.Debug);
                    }

                    progress?.Report(new AssetScanProgress
                    {
                        TotalAssets = assetFilePaths.Count,
                        ProcessedAssets = processedCount,
                        ExtractedTexts = allExtractedTexts.Count,
                        CurrentAsset = Path.GetFileName(assetPath)
                    });
                }
                catch (Exception ex)
                {
                    processedCount++;
                    skippedCount++;

                    await _logService.LogAsync(
                        $"跳过文件 {Path.GetFileName(assetPath)}: {ex.Message}",
                        LogLevel.Debug);
                    // 继续处理下一个文件
                }
            }

            await _logService.LogAsync(
                $"文本提取完成！统计信息：",
                LogLevel.Info);
            await _logService.LogAsync(
                $"  - 候选文件: {assetFilePaths.Count} 个",
                LogLevel.Info);
            await _logService.LogAsync(
                $"  - 有效资产文件: {successCount} 个",
                LogLevel.Info);
            await _logService.LogAsync(
                $"  - 跳过的文件: {skippedCount} 个",
                LogLevel.Info);
            await _logService.LogAsync(
                $"  - 提取文本总数: {allExtractedTexts.Count} 条",
                LogLevel.Info);

            return allExtractedTexts;
        }

        /// <summary>
        /// 从单个资产文件中提取文本
        /// 支持 Bundle 文件和普通资产文件
        /// </summary>
        private async Task<List<ExtractedTextEntry>> ExtractFromSingleAssetAsync(
            string assetFilePath,
            CancellationToken cancellationToken)
        {
            if (_assetsManager == null)
                throw new InvalidOperationException("AssetsManager 未初始化");

            var extractedTexts = new List<ExtractedTextEntry>();

            try
            {
                // 尝试作为 Bundle 文件加载（自动解压缩）
                BundleFileInstance? bundleInstance = null;
                AssetsFileInstance? assetsFileInstance = null;

                try
                {
                    // 使用 unpackIfPacked=true 自动解压缩 Bundle
                    bundleInstance = _assetsManager.LoadBundleFile(assetFilePath, unpackIfPacked: true);

                    // 记录 Bundle 信息
                    var dirCount = bundleInstance.file.BlockAndDirInfo.DirectoryInfos.Count;
                    await _logService.LogAsync(
                        $"成功加载 Bundle 文件: {Path.GetFileName(assetFilePath)} (包含 {dirCount} 个文件)",
                        LogLevel.Info);

                    // 遍历 Bundle 内的文件
                    for (int i = 0; i < dirCount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var dirInfo = bundleInstance.file.BlockAndDirInfo.DirectoryInfos[i];

                            // 跳过非 .assets 资源文件（如 .resS, .resource 等）
                            if (!dirInfo.Name.EndsWith(".assets", StringComparison.OrdinalIgnoreCase))
                            {
                                await _logService.LogAsync(
                                    $"跳过 Bundle 内资源文件: {dirInfo.Name}",
                                    LogLevel.Debug);
                                continue;
                            }

                            // 加载并提取资产文件
                            await _logService.LogAsync(
                                $"提取 Bundle 内资产文件: {dirInfo.Name}",
                                LogLevel.Debug);

                            assetsFileInstance = _assetsManager.LoadAssetsFileFromBundle(bundleInstance, i);
                            var texts = await ExtractFromAssetsFileAsync(assetsFileInstance, assetFilePath);
                            extractedTexts.AddRange(texts);

                            await _logService.LogAsync(
                                $"从 {dirInfo.Name} 提取 {texts.Count} 条文本",
                                LogLevel.Debug);
                        }
                        catch (Exception ex)
                        {
                            await _logService.LogAsync(
                                $"提取 Bundle 内资产失败 (索引 {i}): {ex.Message}",
                                LogLevel.Warning);
                        }
                    }

                    await _logService.LogAsync(
                        $"Bundle 文件提取完成: {Path.GetFileName(assetFilePath)}, 共提取 {extractedTexts.Count} 条文本",
                        LogLevel.Info);
                }
                catch (Exception ex)
                {
                    // 不是 Bundle 文件或加载失败，尝试作为普通资产文件
                    await _logService.LogAsync(
                        $"作为 Bundle 加载失败，尝试作为普通资产文件: {ex.Message}",
                        LogLevel.Debug);

                    try
                    {
                        // 加载普通资产文件（使用 FileShare.ReadWrite 允许 XUnity 并发访问）
                        await using var fileStream = new FileStream(
                            assetFilePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite,
                            4096,
                            FileOptions.Asynchronous);

                        assetsFileInstance = _assetsManager.LoadAssetsFile(fileStream, assetFilePath, true);
                        var texts = await ExtractFromAssetsFileAsync(assetsFileInstance, assetFilePath);
                        extractedTexts.AddRange(texts);

                        await _logService.LogAsync(
                            $"从普通资产文件提取 {texts.Count} 条文本: {Path.GetFileName(assetFilePath)}",
                            LogLevel.Info);
                    }
                    catch (Exception)
                    {
                        // 既不是 Bundle 也不是普通资产文件，可能是其他类型的文件
                        // 降低日志级别为 Debug，避免大量 Warning
                        await _logService.LogAsync(
                            $"跳过非资产文件: {Path.GetFileName(assetFilePath)}",
                            LogLevel.Debug);
                    }
                }
            }
            catch (Exception ex)
            {
                // 外层异常捕获（文件访问异常等）
                await _logService.LogAsync(
                    $"处理文件时出错: {Path.GetFileName(assetFilePath)} - {ex.Message}",
                    LogLevel.Debug);
            }

            return extractedTexts;
        }

        /// <summary>
        /// 从 AssetsFileInstance 中提取文本
        /// </summary>
        private async Task<List<ExtractedTextEntry>> ExtractFromAssetsFileAsync(
            AssetsFileInstance assetsFile,
            string sourceAsset)
        {
            if (_assetsManager == null)
                throw new InvalidOperationException("AssetsManager 未初始化");

            var extractedTexts = new List<ExtractedTextEntry>();

            try
            {
                // 尝试为该资产文件加载对应版本的 ClassDatabase（参考 UABEANext）
                TryLoadClassDatabaseFromPackage(assetsFile.file);

                // 提取 TextAsset
                if (_config.ScanTextAssets)
                {
                    var textAssets = assetsFile.file.GetAssetsOfType(AssetClassID.TextAsset);
                    foreach (var assetInfo in textAssets)
                    {
                        try
                        {
                            var baseField = _assetsManager.GetBaseField(assetsFile, assetInfo);
                            var name = baseField["m_Name"].IsDummy ? "" : baseField["m_Name"].AsString;

                            // 关键修复：m_Script 是 byte[] 类型，不是 string（参考 UABEANext）
                            var scriptField = baseField["m_Script"];
                            if (scriptField.IsDummy)
                                continue;

                            byte[] byteData = scriptField.AsByteArray;
                            if (byteData == null || byteData.Length == 0)
                                continue;

                            // 解码为 UTF-8 文本
                            string text = Encoding.UTF8.GetString(byteData);

                            if (!string.IsNullOrWhiteSpace(text) && IsValidText(text))
                            {
                                extractedTexts.Add(new ExtractedTextEntry
                                {
                                    Text = text,
                                    SourceAsset = sourceAsset,
                                    RelativeSourcePath = GetRelativePath(sourceAsset),
                                    AssetType = "TextAsset",
                                    FieldName = $"m_Script ({name})"
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            await _logService.LogAsync(
                                $"提取 TextAsset 失败: {ex.Message}",
                                LogLevel.Debug);
                        }
                    }
                }

                // 提取 MonoBehaviour
                if (_config.ScanMonoBehaviours)
                {
                    // 检查并设置 MonoTempGenerator（参考 UABEANext）
                    await CheckAndSetMonoTempGeneratorsAsync(assetsFile);

                    var monoBehaviours = assetsFile.file.GetAssetsOfType(AssetClassID.MonoBehaviour);
                    int monoBehaviourCount = 0;
                    int textFieldsFound = 0;

                    foreach (var assetInfo in monoBehaviours)
                    {
                        // 安全地获取 BaseField（内部会捕获 MonoCecil 异常）
                        var baseField = TryGetBaseFieldSafe(assetsFile, assetInfo);
                        if (baseField == null)
                            continue; // 跳过无法解析的 MonoBehaviour

                        monoBehaviourCount++;

                        try
                        {
                            // 遍历配置的字段名
                            foreach (var fieldName in _config.MonoBehaviourFields)
                            {
                                // 尝试多种字段名变体
                                var field = TryGetFieldWithVariants(baseField, fieldName);

                                if (field != null && !field.IsDummy && field.TypeName == "string")
                                {
                                    var text = field.AsString;
                                    if (!string.IsNullOrWhiteSpace(text) && IsValidText(text))
                                    {
                                        extractedTexts.Add(new ExtractedTextEntry
                                        {
                                            Text = text,
                                            SourceAsset = sourceAsset,
                                            RelativeSourcePath = GetRelativePath(sourceAsset),
                                            AssetType = "MonoBehaviour",
                                            FieldName = fieldName
                                        });
                                        textFieldsFound++;

                                        await _logService.LogAsync(
                                            $"找到文本字段 '{fieldName}' (PathId: {assetInfo.PathId}): {text.Substring(0, Math.Min(50, text.Length))}...",
                                            LogLevel.Debug);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // 字段访问异常（NullReferenceException已在TryGetBaseFieldSafe中处理）
                            await _logService.LogAsync(
                                $"提取 MonoBehaviour 字段失败 (PathId: {assetInfo.PathId}): {ex.Message}",
                                LogLevel.Debug);
                        }
                    }

                    if (monoBehaviourCount > 0)
                    {
                        await _logService.LogAsync(
                            $"资产文件 {Path.GetFileName(sourceAsset)}: 扫描 {monoBehaviourCount} 个 MonoBehaviour，找到 {textFieldsFound} 个文本字段",
                            LogLevel.Debug);
                    }
                }

                // 提取 GameObject 名称（可选）
                if (_config.ScanGameObjectNames)
                {
                    var gameObjects = assetsFile.file.GetAssetsOfType(AssetClassID.GameObject);
                    foreach (var assetInfo in gameObjects)
                    {
                        try
                        {
                            var baseField = _assetsManager.GetBaseField(assetsFile, assetInfo);
                            var name = baseField["m_Name"].IsDummy ? "" : baseField["m_Name"].AsString;

                            if (!string.IsNullOrWhiteSpace(name) && IsValidText(name))
                            {
                                extractedTexts.Add(new ExtractedTextEntry
                                {
                                    Text = name,
                                    SourceAsset = sourceAsset,
                                    RelativeSourcePath = GetRelativePath(sourceAsset),
                                    AssetType = "GameObject",
                                    FieldName = "m_Name"
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            await _logService.LogAsync(
                                $"提取 GameObject 名称失败: {ex.Message}",
                                LogLevel.Debug);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _logService.LogAsync(
                    $"从资产文件提取文本失败: {ex.Message}",
                    LogLevel.Warning);
            }

            return extractedTexts;
        }

        /// <summary>
        /// 获取相对于游戏目录的路径
        /// </summary>
        private string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(_gameDirectory) || string.IsNullOrEmpty(fullPath))
                return fullPath;

            try
            {
                return Path.GetRelativePath(_gameDirectory, fullPath);
            }
            catch
            {
                return fullPath;
            }
        }

        /// <summary>
        /// 验证文本是否符合提取规则
        /// </summary>
        private bool IsValidText(string text)
        {
            // 长度检查
            if (text.Length < _config.MinTextLength || text.Length > _config.MaxTextLength)
                return false;

            // 应用排除规则
            foreach (var pattern in _config.ExcludePatterns)
            {
                try
                {
                    if (Regex.IsMatch(text, pattern))
                        return false;
                }
                catch (Exception ex)
                {
                    _logService.Log($"正则表达式错误 ({pattern}): {ex.Message}", LogLevel.Warning);
                }
            }

            // 语言过滤
            if (!MatchesLanguageFilter(text))
                return false;

            return true;
        }

        /// <summary>
        /// 检查文本是否匹配语言过滤器
        /// </summary>
        private bool MatchesLanguageFilter(string text)
        {
            var filter = _config.SourceLanguageFilter;

            return filter switch
            {
                "全部语言" => true,
                "中日韩（CJK）" => HasCJKCharacters(text),
                "简体中文" => HasSimplifiedChineseCharacters(text),
                "繁体中文" => HasTraditionalChineseCharacters(text),
                "日语" => HasJapaneseCharacters(text),
                "英语" => HasEnglishCharacters(text),
                "韩语" => HasKoreanCharacters(text),
                "俄语" => HasRussianCharacters(text),
                _ => true // 未知过滤器，默认通过
            };
        }

        /// <summary>
        /// 检测文本是否包含中日韩字符
        /// </summary>
        private static bool HasCJKCharacters(string text)
        {
            // CJK 统一表意文字: \u4e00-\u9fff
            // 日文平假名: \u3040-\u309f
            // 日文片假名: \u30a0-\u30ff
            // 韩文: \uac00-\ud7af
            return Regex.IsMatch(text, @"[\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\uac00-\ud7af]");
        }

        /// <summary>
        /// 检测文本是否包含简体中文字符
        /// </summary>
        private static bool HasSimplifiedChineseCharacters(string text)
        {
            // 简体中文常用字范围（CJK 统一表意文字）
            // 注：简繁体字符有重叠，这里使用启发式检测
            return Regex.IsMatch(text, @"[\u4e00-\u9fa5]");
        }

        /// <summary>
        /// 检测文本是否包含繁体中文字符
        /// </summary>
        private static bool HasTraditionalChineseCharacters(string text)
        {
            // 繁体中文扩展区（包含繁体专用字）
            return Regex.IsMatch(text, @"[\u4e00-\u9fff]");
        }

        /// <summary>
        /// 检测文本是否包含日语字符
        /// </summary>
        private static bool HasJapaneseCharacters(string text)
        {
            // 日文平假名: \u3040-\u309f
            // 日文片假名: \u30a0-\u30ff
            // 日文汉字: \u4e00-\u9faf (部分)
            return Regex.IsMatch(text, @"[\u3040-\u309f\u30a0-\u30ff]");
        }

        /// <summary>
        /// 检测文本是否包含英语字符
        /// </summary>
        private static bool HasEnglishCharacters(string text)
        {
            // 英文字母（大小写）
            return Regex.IsMatch(text, @"[a-zA-Z]");
        }

        /// <summary>
        /// 检测文本是否包含韩语字符
        /// </summary>
        private static bool HasKoreanCharacters(string text)
        {
            // 韩文字母（谚文）: \uac00-\ud7af
            return Regex.IsMatch(text, @"[\uac00-\ud7af]");
        }

        /// <summary>
        /// 检测文本是否包含俄语字符
        /// </summary>
        private static bool HasRussianCharacters(string text)
        {
            // 西里尔字母（俄语）: \u0400-\u04ff
            return Regex.IsMatch(text, @"[\u0400-\u04ff]");
        }

        /// <summary>
        /// 过滤并去重提取的文本
        /// </summary>
        public List<ExtractedTextEntry> FilterAndDeduplicateTexts(List<ExtractedTextEntry> texts)
        {
            // 按文本内容去重（保留第一次出现的来源）
            var uniqueTexts = texts
                .GroupBy(t => t.Text)
                .Select(g => g.First())
                .ToList();

            _logService.Log(
                $"去重前: {texts.Count} 条，去重后: {uniqueTexts.Count} 条",
                LogLevel.Info);

            return uniqueTexts;
        }

        /// <summary>
        /// 尝试从 ClassPackage 中加载与资产文件版本匹配的 ClassDatabase（参考 UABEANext）
        /// </summary>
        private void TryLoadClassDatabaseFromPackage(AssetsFile file)
        {
            if (_assetsManager == null)
                return;

            // 如果已经有 ClassDatabase，跳过
            if (_assetsManager.ClassDatabase != null)
                return;

            var fileVersion = file.Metadata.UnityVersion;

            // 版本号无效，跳过
            if (string.IsNullOrEmpty(fileVersion) || fileVersion == "0.0.0")
                return;

            try
            {
                _assetsManager.LoadClassDatabaseFromPackage(fileVersion);
                _logService.Log(
                    $"已为 Unity {fileVersion} 加载 ClassDatabase",
                    LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logService.Log(
                    $"无法加载 Unity {fileVersion} 的 ClassDatabase: {ex.Message}",
                    LogLevel.Debug);
            }
        }

        /// <summary>
        /// 安全地获取 MonoBehaviour 的 BaseField，捕获所有 MonoCecil 异常
        /// 使用 DebuggerNonUserCode 属性减少 VS 调试中断
        /// </summary>
        [System.Diagnostics.DebuggerNonUserCode]
        [System.Diagnostics.DebuggerStepThrough]
        private AssetTypeValueField? TryGetBaseFieldSafe(AssetsFileInstance fileInst, AssetFileInfo info)
        {
            if (_assetsManager == null)
                return null;

            try
            {
                // 尝试获取 BaseField（内部会调用 MonoCecilTempGenerator）
                // 某些损坏的 MonoBehaviour 会导致 MonoCecil 抛出 NullReferenceException
                return _assetsManager.GetBaseField(fileInst, info);
            }
            catch
            {
                // 捕获所有异常（包括 MonoCecil 的 NullReferenceException）
                // 返回 null 表示该 MonoBehaviour 无法解析
                return null;
            }
        }

        /// <summary>
        /// 尝试使用多种字段名变体获取字段
        /// 支持：原始名称、m_前缀、首字母大写等变体
        /// </summary>
        private AssetTypeValueField? TryGetFieldWithVariants(AssetTypeValueField baseField, string fieldName)
        {
            // 1. 尝试原始字段名
            try
            {
                var field = baseField[fieldName];
                if (field != null && !field.IsDummy)
                    return field;
            }
            catch { }

            // 2. 如果字段名不以 m_ 开头，尝试添加 m_ 前缀
            if (!fieldName.StartsWith("m_", StringComparison.Ordinal))
            {
                try
                {
                    var field = baseField[$"m_{fieldName}"];
                    if (field != null && !field.IsDummy)
                        return field;
                }
                catch { }

                // 3. 尝试 m_ + 首字母大写
                if (fieldName.Length > 0)
                {
                    try
                    {
                        var capitalized = char.ToUpper(fieldName[0]) + fieldName.Substring(1);
                        var field = baseField[$"m_{capitalized}"];
                        if (field != null && !field.IsDummy)
                            return field;
                    }
                    catch { }
                }
            }

            // 4. 如果字段名以 m_ 开头，尝试移除 m_ 前缀
            if (fieldName.StartsWith("m_", StringComparison.Ordinal) && fieldName.Length > 2)
            {
                try
                {
                    var withoutPrefix = fieldName.Substring(2);
                    var field = baseField[withoutPrefix];
                    if (field != null && !field.IsDummy)
                        return field;
                }
                catch { }

                // 5. 尝试移除 m_ 后首字母小写
                try
                {
                    var withoutPrefix = fieldName.Substring(2);
                    if (withoutPrefix.Length > 0)
                    {
                        var lowercased = char.ToLower(withoutPrefix[0]) + withoutPrefix.Substring(1);
                        var field = baseField[lowercased];
                        if (field != null && !field.IsDummy)
                            return field;
                    }
                }
                catch { }
            }

            // 未找到匹配的字段
            return null;
        }

        /// <summary>
        /// 设置 MonoTempGenerator 用于 MonoBehaviour 反序列化（参考 UABEANext）
        /// 当资产文件没有 TypeTree 时需要此生成器
        /// 支持 Mono 和 IL2CPP 两种后端
        /// </summary>
        private async Task<bool> SetupMonoTempGeneratorAsync()
        {
            if (_assetsManager == null || _monoTempGeneratorSet || string.IsNullOrEmpty(_gameDirectory))
                return false;

            _monoTempGeneratorSet = true;

            try
            {
                // Unity 游戏的 Managed 文件夹可能在多个位置，按优先级尝试
                List<string> searchPaths = new List<string>();

                // 1. 优先：*_Data/Managed（Unity 标准位置）
                var dataDirs = Directory.GetDirectories(_gameDirectory, "*_Data", SearchOption.TopDirectoryOnly);
                foreach (var dataDir in dataDirs)
                {
                    searchPaths.Add(Path.Combine(dataDir, "Managed"));
                }

                // 2. 回退：游戏根目录/Managed（某些特殊打包方式）
                searchPaths.Add(Path.Combine(_gameDirectory, "Managed"));

                // 尝试每个路径检测 Mono 后端
                foreach (var managedDir in searchPaths)
                {
                    if (Directory.Exists(managedDir))
                    {
                        var dllFiles = Directory.GetFiles(managedDir, "*.dll");
                        if (dllFiles.Length > 0)
                        {
                            _assetsManager.MonoTempGenerator = new MonoCecilTempGenerator(managedDir);
                            await _logService.LogAsync(
                                $"已设置 MonoCecilTempGenerator（Mono 后端）: {managedDir} ({dllFiles.Length} 个 DLL)",
                                LogLevel.Info);
                            return true;
                        }
                    }
                }

                // 3. 回退：检测 IL2CPP 后端
                // 从游戏根目录或 *_Data 目录查找 IL2CPP 文件
                List<string> il2cppSearchPaths = new List<string>();

                // 先尝试 *_Data 目录（标准位置）
                foreach (var dataDir in dataDirs)
                {
                    il2cppSearchPaths.Add(dataDir);
                }

                // 再尝试游戏根目录
                il2cppSearchPaths.Add(_gameDirectory);

                foreach (var searchPath in il2cppSearchPaths)
                {
                    var il2cppFiles = FindCpp2IlFiles.Find(searchPath);
                    if (il2cppFiles.success)
                    {
                        _assetsManager.MonoTempGenerator = new Cpp2IlTempGenerator(
                            il2cppFiles.metaPath!,
                            il2cppFiles.asmPath!
                        );
                        await _logService.LogAsync(
                            $"已设置 Cpp2IlTempGenerator（IL2CPP 后端）: {Path.GetFileName(il2cppFiles.metaPath)} + {Path.GetFileName(il2cppFiles.asmPath)}",
                            LogLevel.Info);
                        return true;
                    }
                }

                // 未找到 Managed 文件夹或 IL2CPP 文件，记录尝试的路径
                var attemptedPaths = string.Join(", ", searchPaths.Select(p => $"'{Path.GetFileName(Path.GetDirectoryName(p))}/Managed'"));
                await _logService.LogAsync(
                    $"未找到 Managed 文件夹或 IL2CPP 元数据（已尝试: {attemptedPaths}），MonoBehaviour 提取将依赖 ClassDatabase 和 TypeTree",
                    LogLevel.Warning);
                return false;
            }
            catch (Exception ex)
            {
                await _logService.LogAsync(
                    $"设置 MonoTempGenerator 失败: {ex.Message}",
                    LogLevel.Warning);
                return false;
            }
        }

        /// <summary>
        /// 检查并设置 MonoTempGenerator（仅在需要时调用，参考 UABEANext）
        /// 从资产文件所在目录查找 Managed 文件夹或 IL2CPP 元数据
        /// 支持 Mono 和 IL2CPP 两种后端
        /// </summary>
        private async Task CheckAndSetMonoTempGeneratorsAsync(AssetsFileInstance fileInst)
        {
            if (_assetsManager == null || _monoTempGeneratorSet)
                return;

            // 仅当文件没有 TypeTree 时才需要 MonoTempGenerator
            if (fileInst.file.Metadata.TypeTreeEnabled)
                return;

            _monoTempGeneratorSet = true;

            try
            {
                // 使用 fileInst.path 获取资产文件实际路径（参考 UABEANext 的 PathUtils）
                string assetPath = fileInst.path;

                // 特殊处理内置资源（参考 UABEANext PathUtils.cs:47-50）
                if (fileInst.name == "unity default resources" ||
                    fileInst.name == "unity_builtin_extra")
                {
                    var parentDir = Path.GetDirectoryName(assetPath);
                    if (!string.IsNullOrEmpty(parentDir))
                        assetPath = parentDir;
                }

                // 从资产文件路径获取所在目录（通常是 *_Data 目录）
                var assetDir = Path.GetDirectoryName(assetPath);
                if (string.IsNullOrEmpty(assetDir))
                {
                    // 如果无法从资产文件获取目录，回退到游戏根目录搜索
                    await SetupMonoTempGeneratorAsync();
                    return;
                }

                // 方法 1：在资产文件所在目录查找 Mono Managed 文件夹
                var managedDir = Path.Combine(assetDir, "Managed");
                if (Directory.Exists(managedDir))
                {
                    var dllFiles = Directory.GetFiles(managedDir, "*.dll");
                    if (dllFiles.Length > 0)
                    {
                        _assetsManager.MonoTempGenerator = new MonoCecilTempGenerator(managedDir);
                        await _logService.LogAsync(
                            $"已设置 MonoCecilTempGenerator（Mono 后端）: {managedDir} ({dllFiles.Length} 个 DLL)",
                            LogLevel.Info);
                        return;
                    }
                }

                // 方法 2：在资产文件所在目录查找 IL2CPP 文件
                var il2cppFiles = FindCpp2IlFiles.Find(assetDir);
                if (il2cppFiles.success)
                {
                    _assetsManager.MonoTempGenerator = new Cpp2IlTempGenerator(
                        il2cppFiles.metaPath!,
                        il2cppFiles.asmPath!
                    );
                    await _logService.LogAsync(
                        $"已设置 Cpp2IlTempGenerator（IL2CPP 后端）: {Path.GetFileName(il2cppFiles.metaPath)} + {Path.GetFileName(il2cppFiles.asmPath)}",
                        LogLevel.Info);
                    return;
                }

                // 如果资产目录中找不到，回退到游戏根目录搜索
                await SetupMonoTempGeneratorAsync();
            }
            catch (Exception ex)
            {
                await _logService.LogAsync(
                    $"检查 MonoTempGenerator 失败: {ex.Message}",
                    LogLevel.Debug);
            }
        }

        public void Dispose()
        {
            _assetsManager?.MonoTempGenerator?.Dispose();
            _assetsManager?.UnloadAll();
        }
    }
}
