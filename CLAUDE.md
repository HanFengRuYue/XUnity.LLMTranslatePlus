# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**XUnity大语言模型翻译Plus** - WinUI 3 application integrating with XUnity.AutoTranslator for AI-powered game text translation.

- **Framework**: .NET 9.0 + WinUI 3
- **Deployment**: Framework-Dependent Single-File (~40MB exe)
- **Runtime Requirements**: .NET 9 Desktop Runtime + Windows App SDK 1.8 Runtime
- **Language**: C# 13.0 with nullable reference types

## Build Commands

```bash
# Development
dotnet build
dotnet run --project XUnity-LLMTranslatePlus/XUnity-LLMTranslatePlus.csproj

# Release (Recommended - builds all architectures)
.\Build-Release.ps1

# Manual publish (single architecture)
dotnet publish --configuration Release --runtime win-x64 --self-contained false /p:PublishSingleFile=true
```

### Release Build Script (Build-Release.ps1)

**Features**:
- Builds three architectures simultaneously: **x86**, **x64**, **arm64**
- Automatically compresses each build with 7-Zip (maximum compression)
- Outputs both executables and distribution-ready ZIP files

**Requirements**:
- 7-Zip installed at `C:\Program Files\7-Zip\7z.exe`
- If 7-Zip is not found, script will exit with error

**Output Structure**:
```
Release/
├── win-x86/
│   └── XUnity-LLMTranslatePlus.exe
├── win-x64/
│   └── XUnity-LLMTranslatePlus.exe
├── win-arm64/
│   └── XUnity-LLMTranslatePlus.exe
├── XUnity-LLMTranslatePlus-win-x86.zip   (for distribution)
├── XUnity-LLMTranslatePlus-win-x64.zip   (for distribution)
└── XUnity-LLMTranslatePlus-win-arm64.zip (for distribution)
```

**ZIP Contents**: Each archive contains only the single EXE file (no folder structure), allowing users to extract directly.

**Compression**: ~40MB EXE → ~10MB ZIP (~75% compression ratio)

**Options**:
- `-SkipClean`: Skip cleaning previous builds (faster for incremental builds)

**Critical**: Single-file deployment requires environment variable in `App.xaml.cs`:
```csharp
Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
```

## Core Architecture Patterns

### 1. Async Locking (ConfigService.cs, TranslationService.cs)
- **Never** use `lock` in async methods - use `SemaphoreSlim`
- **Never** call methods acquiring the same lock while holding it (causes deadlock)

```csharp
await _semaphore.WaitAsync(cancellationToken);
try { /* critical section */ }
finally { _semaphore.Release(); }
```

### 2. High-Performance Batching with Channel<T>
**LogService.cs**: Batches log writes (50 entries or 2s)
**FileMonitorService.cs**: Batches file writes (2 translations or 0.5s) to prevent XUnity conflicts while minimizing write latency

**Critical Patterns**:
- Initialize Channel BEFORE any code writes to it
- Use `WaitToReadAsync()` + `TryRead()` with `Task.WhenAny()` (not `ReadAsync()`)
- Store `.AsTask()` result in variable - calling multiple times creates different Task instances
- Disable FileSystemWatcher during writes (300ms stabilization delay) to prevent infinite loops
- **Use `TryWrite()` instead of `WriteAsync()`** when writing to Channels that may be closed (prevents exceptions during shutdown)

```csharp
// ✅ Correct - graceful handling when Channel closed
if (!_writeQueue.Writer.TryWrite(batch))
{
    await _logService.LogAsync("Queue closed, exiting", LogLevel.Debug);
    break;
}

// ❌ Wrong - throws ChannelClosedException during shutdown
await _writeQueue.Writer.WriteAsync(batch, cancellationToken);
```

### 3. Thread-Safe Context Cache (TranslationService.cs)
Context feature stores last N translations (default 10, configurable 1-100) for AI reference.

```csharp
private readonly List<string> _contextCache = new List<string>();
private readonly object _contextLock = new object();

// All operations protected
lock (_contextLock) { /* read/write */ }
```

**Always** pass `AppConfig` to `AddToContext()` - cache size is dynamic via `config.ContextLines`.

### 4. Concurrent File Access (TextFileParser.cs)
**Critical**: Use `FileShare.ReadWrite` when reading files XUnity may write to:

```csharp
await using var fileStream = new FileStream(
    path, FileMode.Open, FileAccess.Read,
    FileShare.ReadWrite,  // Allows XUnity to write simultaneously
    4096, FileOptions.Asynchronous);
```

Default `File.ReadAllLinesAsync()` uses exclusive locking and will cause "file in use" errors.

### 5. Security & Performance
- **HttpClient**: Always use `IHttpClientFactory`, never static instances (ApiClient.cs)
- **Path Validation**: All paths MUST go through `PathValidator` (prevents path traversal)
- **API Keys**: Encrypted with Windows DPAPI, stored with `"ENC:"` prefix (SecureDataProtection.cs)
- **Regex**: Use `[GeneratedRegex]` attribute for 3-140x performance (EscapeCharacterHandler.cs)
- **String Ops**: Prefer `AsSpan()` over `Substring()` in loops

## XUnity Translation File Format

**Critical**: Escape sequences (`\n`, `\r`, `\t`) are stored as **literal two characters**, NOT actual newlines.

```
// Comments start with //
key1=value1
key2="value with spaces"
```

**Untranslated Detection**:
- Basic: `key == value`
- Advanced: `IsCorruptedTranslation()` detects lost escape sequences

## Translation Pipeline (TranslationService.cs)

**CRITICAL**: Terminology matching happens FIRST using original text (before escape processing) to prevent escape characters from breaking term matching.

1. Increment `TranslatingCount` (thread-safe counter)
2. **Check exact terminology match using original text** → If matched, return translation immediately (skip steps 3-7)
3. Extract special characters → placeholders (e.g., `【SPECIAL_0】`) - ONLY if step 2 fails
4. Build terminology reference for partial matches
5. Build context (N recent translations, thread-safe with `_contextLock`)
6. Build system prompt (replaces `{目标语言}`, `{原文}`, `{术语}`, `{上下文}`)
7. Call API with **retry + exponential backoff** (see API Retry Strategy below)
8. Apply terminology post-processing (partial term replacements)
9. Restore special characters intelligently
10. Update context cache (thread-safe: `AddToContext(original, translated, config)`)
11. Smart terminology extraction (async background with `Task.Run`, non-blocking)
12. Decrement `TranslatingCount` (in `finally` block)

**Statistics**: `TotalTranslated` uses `HashSet<string>` for unique count deduplication.

### API Retry Strategy (ApiClient.cs)

**Rate Limiting (HTTP 429)**:
- Exponential backoff with random jitter: 2s → 4s → 8s → 16s (capped at 30s)
- Jitter: ±20% randomization to prevent thundering herd
- Does NOT count toward error threshold (automatically recoverable)

**Other Errors**:
- Linear backoff: 2s → 4s → 6s
- Counts toward `ErrorThreshold` configuration

## File Monitoring Pipeline (FileMonitorService.cs)

**Two-Queue Design** (cannot be merged):
1. **`_pendingTexts` Queue** → Detected untranslated, waiting for API
2. **`_translatingCount` Counter** → Active API calls in flight

```
File change → 200ms delay → Parse → _pendingTexts → Consumer → API → _translatingCount → _writeQueue → Batch write
```

**Concurrency**: Default 3, configurable 1-100. Batch size = `MaxConcurrentTranslations * 2`.

## Terminology Management

**Multi-File Architecture** (TerminologyService.cs, SmartTerminologyService.cs):

```
AppData/XUnity-LLMTranslatePlus/
├── terms.csv                    # Default terminology file
├── config.json
└── Terminologies/               # Additional terminology files
    ├── default.csv
    ├── game-specific.csv
    └── custom.csv
```

**Key Patterns**:
- All terminology operations protected by `_lockObject` (thread-safe)
- Current file tracked in `AppConfig.CurrentTerminologyFile`
- Default file cannot be deleted via UI
- Files stored in `AppData/XUnity-LLMTranslatePlus/Terminologies/` folder

**Smart Extraction** (SmartTerminologyService.cs):
- AI-powered extraction of proper nouns during translation
- Runs asynchronously via `Task.Run` (non-blocking)
- **Saves to current terminology file** via `GetCurrentTerminologyFilePath()` which reads `AppConfig.CurrentTerminologyFile`
- Requires `ConfigService` dependency to determine save path
- Default priority: 50 for extracted terms
- Duplicate prevention: Case-insensitive check with `IsTermExists()`
- Tracks processed texts with `HashSet<string>` to avoid re-extraction
- JSON response parsing handles markdown code blocks (`\`\`\`json`)
- Failures logged as warnings, don't interrupt translation flow

**Apply Order**: Sorted by Priority (descending) → Length (descending, longer terms first)

## Dependency Injection (App.xaml.cs)

All services are singletons:
```csharp
services.AddHttpClient();  // Required for ApiClient
services.AddSingleton<LogService>();
services.AddSingleton<ConfigService>();
services.AddSingleton<TerminologyService>();
services.AddSingleton<ApiClient>();
services.AddSingleton<SmartTerminologyService>();
services.AddSingleton<TranslationService>();
services.AddSingleton<FileMonitorService>();
services.AddSingleton<TextEditorService>();
```

Pages resolve via: `App.GetService<LogService>()`

### Unity Asset Extraction (NEW)

**Architecture** (AssetScannerService.cs, AssetTextExtractor.cs, PreTranslationService.cs):
- **Mixed-mode approach**: Pre-extract static texts from Unity assets + Real-time monitoring for dynamic texts
- Uses **AssetsTools.NET 3.0.2** and **AssetsTools.NET.MonoCecil 3.0.2** libraries

**Workflow**:
```
1. Scan game directory → Find .assets, .bundle, .unity3d files
2. Extract texts → TextAsset, MonoBehaviour fields, GameObject names
3. Filter texts → CJK character detection, regex exclusions
4. Translate batch → Use existing TranslationService with Channel<T> concurrency
5. Merge → Combine with existing XUnity translation file
6. Real-time monitoring → FileMonitorService handles dynamic texts
```

**Key Components**:
- **AssetScannerService**: Scans game directory recursively for Unity asset files
- **AssetTextExtractor**: Extracts text using AssetsTools.NET, filters with configurable rules
- **PreTranslationService**: Coordinates scan→extract→translate→merge pipeline

**Configuration** (AssetExtractionConfig):
- `ScanTextAssets`: Extract TextAsset.m_Script (dialogues, configs)
- `ScanMonoBehaviours`: Extract custom script fields (configurable field names)
- `MonoBehaviourFields`: List of field names to extract (text, dialogText, description, etc.)
  - Default fields: text, dialogText, description, itemName, tooltipText, message, content, title, subtitle
  - User-configurable via UI field name management
- `SourceLanguageFilter`: Language filter for text extraction (default: "全部语言")
  - Options: 全部语言, 中日韩（CJK）, 简体中文, 繁体中文, 日语, 英语, 韩语, 俄语
  - Uses Unicode range detection for precise filtering
- `ExcludePatterns`: Regex patterns to filter out paths, variable names, etc.
- `OverwriteExisting`: Whether to re-translate existing translations
- `ClassDatabasePath`: User-selected path to classdata.tpk file (optional, configured via UI file picker)

**Critical Implementation Details**:
- `IsDummy` is a **property**, not a method: `field.IsDummy` (not `field.IsDummy()`)
- **ClassDatabase Loading** (UPDATED 2025-01):
  - **Embedded Resource Priority**: classdata.tpk is now embedded in the executable as `Resources/classdata.tpk`
  - Uses `LoadClassPackage()` NOT `LoadClassDatabase()` - loads multi-version .tpk package
  - **Simplified Loading Priority** (reliable methods only):
    1. **Program Embedded Resource** (PRIMARY) - Extracted from assembly to temp file, always available
    2. **User-Configured Path** (BACKUP) - Optional custom classdata.tpk via UI file picker
  - **Removed unreliable methods**: AppData default path and asset built-in TypeTree fallback
  - Fails fast with clear error message if both methods fail
  - Then calls `LoadClassDatabaseFromPackage(unityVersion)` for each asset file to load version-specific types
  - Performance optimizations enabled: `UseTemplateFieldCache`, `UseQuickLookup`
- **TextAsset extraction** (CRITICAL):
  - `m_Script` field is **byte[] NOT string** - use `AsByteArray` then `Encoding.UTF8.GetString()`
  - Previous `AsString` approach failed silently, causing 0 text extraction
  - Pattern: `var bytes = field["m_Script"].AsByteArray; string text = Encoding.UTF8.GetString(bytes);`
- **MonoBehaviour extraction with MonoCecilTempGenerator**:
  - **CRITICAL**: Use `fileInst.path` NOT string parameter for Managed folder path (AssetTextExtractor.cs:586-587)
  - Managed folder search paths (AssetTextExtractor.cs:519-563):
    1. `*_Data/Managed` (Unity standard, e.g., `GameName_Data/Managed`)
    2. `GameRoot/Managed` (fallback for special packaging)
  - MonoCecilTempGenerator requires game DLL files from Managed folder
  - **Special handling** for internal resources: `"unity default resources"`, `"unity_builtin_extra"` (move up one directory level)
  - Only set MonoTempGenerator when `TypeTreeEnabled == false` (optimization)
- **Handling Third-Party Library Exceptions** (AssetTextExtractor.cs:490-513):
  - AssetsTools.NET.MonoCecil may throw NullReferenceException for corrupted MonoBehaviour types
  - Use `[DebuggerNonUserCode]` and `[DebuggerStepThrough]` attributes to reduce VS debugging interruptions
  - Wrap all MonoCecil calls in `TryGetBaseFieldSafe()` method with try-catch returning null
  - This is expected behavior - some MonoBehaviour instances may reference deleted/corrupted scripts
- **UI Features** (AssetExtractionPage - UPDATED 2025-01):
  - **Page State Persistence**: Uses `NavigationCacheMode.Required` to preserve all state when navigating away
    - Extracted texts list retained
    - Scan configuration preserved
    - User selections maintained
    - Statistics persist
  - **Non-blocking UI**: All heavy operations run in `Task.Run` with loading overlay
    - ProgressRing with status messages
    - Background thread execution prevents UI freeze
    - Cancellation support via CancellationToken
  - **Field Name Management**: Similar to terminology management UI
    - ListView with editable TextBox for each field
    - Add/Delete buttons with selection management
    - Auto-save with 800ms debounce
  - **Language Filtering**: ComboBox for source language selection (全部语言, 中日韩, 简中, 繁中, 日语, 英语, 韩语, 俄语)
  - ListView displays extracted texts with: Text, Source File, Asset Type, Field Name
  - Search functionality (Ctrl+F) filters by text content or source file
  - Select All / Deselect All buttons for batch selection
  - Only selected texts are translated (selective translation)
  - Real-time statistics: total/filtered/selected counts
  - ClassDatabase file picker integrated in UI (Browse/Clear buttons) - optional, uses embedded version by default
  - **Operation Log**: Collapsible Expander with styled log entries (timestamp + level badge + message)
  - **Consistent Layout**: Matches other pages (padding, spacing, Expander styles)
- Users can select classdata.tpk file directly in AssetExtractionPage UI (optional - program has embedded version)
- Uses `FileShare.ReadWrite` for reading assets to allow concurrent access
- TextExtractor is `IDisposable` - create with `using` statement
- Batch translation reuses existing Channel<T> + TranslationService architecture

**Pages**:
- AssetExtractionPage - Scan, extract, translate UI with complete text list, search, and selective translation

## UI Patterns

### Custom Title Bar (MainWindow.xaml.cs)
```csharp
// ORDER MATTERS
this.Title = "XUnity大语言模型翻译Plus";
AppWindow.SetIcon("ICON.ico");
ExtendsContentIntoTitleBar = true;
SetTitleBar(AppTitleBar);
```
Title bar element must be non-interactive. Use `ms-appx:///` for embedded assets.

### Event-Driven Updates
Always use `DispatcherQueue.TryEnqueue()` when updating UI from non-UI threads:
```csharp
service.EntryTranslated += (sender, entry) =>
{
    DispatcherQueue.TryEnqueue(() => { /* Update UI */ });
};
```

### Auto-save Debouncing Pattern
All settings pages use auto-save with debouncing to prevent excessive writes:

```csharp
private DispatcherQueueTimer? _autoSaveTimer;
private bool _isLoadingConfig = false;

// Initialize in constructor
_autoSaveTimer = DispatcherQueue.CreateTimer();
_autoSaveTimer.Interval = TimeSpan.FromMilliseconds(800);  // 500ms for simple pages
_autoSaveTimer.Tick += (s, e) =>
{
    _autoSaveTimer.Stop();
    _ = AutoSaveConfigAsync();
};

// Trigger on any control change
private void TriggerAutoSave()
{
    if (_isLoadingConfig || _configService == null) return;
    _autoSaveTimer?.Stop();
    _autoSaveTimer?.Start();  // Reset timer
}
```

**Debounce Intervals**:
- ApiConfigPage: 800ms (complex configuration)
- TerminologyPage: 800ms (complex with file operations)
- TranslationSettingsPage: 500ms (simpler settings)

**Critical**:
- `GetConfigFromUIAsync()` must ALWAYS load fresh config first via `LoadConfigAsync()` to preserve settings from other pages, then only update relevant fields.
- After saving config, update the local `_currentConfig` field to prevent stale data from being saved later:
  ```csharp
  var config = await _configService.LoadConfigAsync();
  config.SomeSetting = newValue;
  await _configService.SaveConfigAsync(config);
  _currentConfig = config;  // ← Critical: Update local cache
  ```

### Pages
- HomePage - Status, start/stop monitoring
- ApiConfigPage - API config, connection test
- TranslationSettingsPage - Game directory, language, context, system prompt
- TerminologyPage - Terminology CRUD, file management, smart extraction toggle
- TextEditorPage - Manual editing, CSV import/export
- AssetExtractionPage - Unity asset scanning, text extraction, selective translation with search
- LogPage - Real-time logs with filtering
- AboutPage - App info, reset config (in footer)

## Critical Gotchas

1. **WinUI 3 Threading**: UI updates MUST use `DispatcherQueue.TryEnqueue()`

2. **XUnity File Format**: Never escape/unescape `\n` as actual newlines - store as literal two characters

3. **SemaphoreSlim Deadlock**: Never call methods acquiring same lock while holding it. ConfigService `LoadConfigAsync` must serialize inline, NOT call `SaveConfigAsync`.

4. **Channel<T> with Task.WhenAny**: Use `WaitToReadAsync()` + `TryRead()`, NOT `ReadAsync()`. Store `.AsTask()` in variable.

5. **FileSystemWatcher Loop**: Disable `EnableRaisingEvents` during file writes, add 300ms delay, re-enable in `finally`.

6. **File Locking**: Use `FileShare.ReadWrite` for reading, `FileShare.Read` for writing. Default methods cause "file in use" errors.

7. **Context Cache**: Protected by `_contextLock`. Never access `_contextCache` without lock. `AddToContext()` requires `AppConfig` parameter.

8. **Single-File Deployment**: Requires `EnableMsixTooling=true` and environment variable setup. Framework-dependent cannot use compression.

9. **TranslatingCount**: Property handles locking internally for reads. Use `_translatingLock` for increment/decrement.

10. **Config Validation**: Ranges must match UI limits. `ContextLines >= 1`, `MaxConcurrentTranslations` 1-100, etc.

11. **Channel Initialization**: Create Channel BEFORE calling methods that write to it or writes silently fail.

12. **Async All The Way**: Never use `.Result` or `.Wait()`. Always propagate `CancellationToken`.

13. **Smart Terminology Extraction**: Runs in background via `Task.Run`, failures don't interrupt translations. Uses `HashSet<string>` to track processed texts (prevents duplicate extraction). JSON response parsing handles markdown code blocks (`\`\`\`json`). Always case-insensitive duplicate check before adding terms.

14. **ConfigService.SaveConfigAsync Completeness**: When adding new fields to `AppConfig`, you MUST add them to the `configToSave` copy in `ConfigService.SaveConfigAsync()`. Missing fields will be reset to defaults on every save. Always verify all AppConfig properties are included.

15. **LoadAndProcessFileAsync Timing**: This method is called BEFORE `_isMonitoring = true` in `StartMonitoringAsync()`. Do not add state checks that depend on `IsMonitoring` being true inside the initial file processing logic, or startup will fail.

16. **Channel Write During Shutdown**: Use `TryWrite()` instead of `WriteAsync()` for Channels that may be closed during shutdown. Check return value and exit gracefully if false.

17. **Translation Pipeline Order**: Terminology matching MUST happen BEFORE escape character processing. If you extract special chars first (e.g., `\n` → `【SPECIAL_0】`), terminology database entries containing `\n` will never match. Always check `FindExactTerm()` with original text first, then proceed with escape processing only if no match found.

18. **Rate Limiting Resilience**: HTTP 429 errors are transient and should use exponential backoff (not linear). They should NOT increment `TotalFailed` or count toward `ErrorThreshold` since they automatically recover with proper delays. Use `RateLimitException` for special handling.

19. **Unity Asset Path Resolution**: When working with AssetsFileInstance, ALWAYS use `fileInst.path` property to get the actual asset file path, NOT the string parameter passed to loading methods. When loading from bundles, the parameter is the bundle path, but `fileInst.path` is the actual .assets file path inside. This is critical for Managed folder discovery (AssetTextExtractor.cs:586-587).

20. **Third-Party Library Exception Handling**: When wrapping third-party library calls that may throw exceptions in debug mode (like AssetsTools.NET.MonoCecil), use `[DebuggerNonUserCode]` and `[DebuggerStepThrough]` attributes on wrapper methods. This tells Visual Studio to skip breaking on first-chance exceptions in that code, improving debugging experience. Combine with try-catch returning null for graceful handling (AssetTextExtractor.cs:494-495).

21. **MonoCecilTempGenerator Path Logic**: The Managed folder is NOT in the game root directory. For Unity games, it's in `GameName_Data/Managed`. Extract path from `fileInst.path`, handle special cases for internal resources (`"unity default resources"`, `"unity_builtin_extra"` need parent directory), and support multiple fallback paths (*_Data/Managed → Root/Managed). Reference UABEANext's PathUtils.GetAssetsFileDirectory() implementation (AssetTextExtractor.cs:573-631).

22. **Page State Persistence** (UPDATED 2025-01): For pages that need to retain state across navigation (like AssetExtractionPage), ALWAYS set `NavigationCacheMode.Required` in the constructor. Without this, all extracted texts, selections, and configurations are lost when user navigates away. Must be set BEFORE page is displayed: `this.NavigationCacheMode = NavigationCacheMode.Required;`

23. **Embedded Resources Loading**: When loading embedded resources (like classdata.tpk), extract to temp file first, don't use stream directly with third-party libraries. Pattern: `var tempPath = Path.Combine(Path.GetTempPath(), "unique_name.tpk"); stream.CopyToAsync(fileStream);` This ensures compatibility with libraries that expect file paths.

24. **Language Filtering Implementation**: Use pattern matching (`switch` expression) with `MatchesLanguageFilter()` method for maintainability. Each language has specific Unicode range check. Always provide "全部语言" (All Languages) default to support multi-language games. Never filter by default unless user explicitly configures it.

25. **Shared Model Classes**: When multiple pages use the same data model (like `LogEntry`), define it ONCE in Models namespace and add necessary UI-specific properties (like `LevelColor` for colored badges). Avoid duplicate class definitions in Views namespace to prevent type conflicts and compilation errors.

26. **TextFileParser.SaveFileAsync Must Append New Translations** (CRITICAL): When saving translations, the method MUST append new entries not present in `_fileLines`. Only iterating through `_fileLines` will silently lose all new translations. Always maintain a `HashSet<string> processedKeys` during file line iteration, then iterate `_translations` dictionary to append any keys NOT in `processedKeys`. Failing to do this causes complete data loss of new translations (TextFileParser.cs:273-368).

27. **XUnity File Format Special Character Escaping** (CRITICAL): Keys and values containing actual special characters (newline `\n`, tab `\t`, etc.) MUST be escaped to literal sequences before writing. Use `EscapeSpecialChars()` to convert actual `\n` character → literal `\n` (backslash + n, two characters). Without escaping, `WriteLineAsync()` splits entries across multiple lines, corrupting the file format. XUnity expects: `\n` = two literal characters, NOT actual newline. Apply escaping in both `SaveFileAsync` and `SaveTranslationsDirectAsync` (TextFileParser.cs:58-72, 357-358, 490-491).

## Performance Notes

- **Concurrent Limits**: Default 3, max 100. Higher values may hit rate limits.
- **Log Verbosity**: `LogLevel.Debug` generates significant I/O.
- **Memory**: TextFileParser keeps file in memory. Large files (>100K entries) may need optimization.
- **UI**: LogPage shows 500 recent entries. HomePage shows 8 recent translations.

## Exception Hierarchy (Exceptions/TranslationException.cs)

- `TranslationException` - Base
- `ApiConnectionException` - Network/HTTP errors
  - `RateLimitException` - HTTP 429 (Too Many Requests), includes optional `RetryAfterSeconds`
- `ApiResponseException` - API parsing errors
- `TranslationTimeoutException` - Timeout errors
- `ConfigurationValidationException` - Config validation
- `FileOperationException` - File I/O errors

**Best Practices**:
- Catch specific types first (e.g., `RateLimitException`), then base types
- `RateLimitException` should trigger exponential backoff, NOT count toward error threshold
- Never swallow exceptions without logging

## Recent Updates

### 2025-01-27: Asset Extraction Page Improvements

**Major Refactoring** - 10 improvements to enhance stability, usability, and consistency:

1. **Page State Persistence** ✓
   - Implemented `NavigationCacheMode.Required` for state preservation
   - Extracted texts, configurations, selections, and statistics now persist across navigation
   - Eliminates need to re-scan after switching pages

2. **Embedded ClassDatabase** ✓
   - classdata.tpk now embedded as `Resources/classdata.tpk` (1.3MB)
   - Simplified loading: Embedded resource (primary) → User path (backup)
   - Removed unreliable AppData and TypeTree fallback methods
   - More stable, predictable behavior

3. **Non-Blocking UI** ✓
   - All heavy operations moved to background threads with `Task.Run`
   - Added loading overlay with ProgressRing and status messages
   - Full cancellation support with CancellationToken
   - UI remains responsive during long scans/extractions

4. **Improved Text List Display** ✓
   - Redesigned to match terminology management page style
   - Multi-column layout: Text (3*) | Source (2*) | Type (100px) | Field (120px)
   - Enhanced search with Ctrl+F support
   - Selection statistics and batch operations

5. **Consistent Layout** ✓
   - Fixed Expander width issues (`HorizontalAlignment="Stretch"`)
   - Unified padding/margins across all pages
   - Standardized title styles and spacing
   - Professional, cohesive design

6. **Unified Operation Log** ✓
   - Styled log entries matching LogPage design
   - Timestamp + colored level badge + message
   - Collapsible Expander with CardBackgroundFillColorDefaultBrush
   - Consistent typography and spacing

7. **Language Filtering** ✓
   - Replaced boolean `IgnoreEnglishOnly` with flexible `SourceLanguageFilter`
   - ComboBox with 8 options: All, CJK, Simplified/Traditional Chinese, Japanese, English, Korean, Russian
   - Unicode range detection for precise filtering
   - Supports games in any language

8. **Field Name Management** ✓
   - Similar UI to terminology management
   - Editable ListView with add/delete functionality
   - Auto-save with 800ms debounce
   - Default fields: text, dialogText, description, itemName, tooltipText, message, content, title, subtitle

9. **Operation Log as Expander** ✓
   - Wrapped in collapsible Expander component
   - Default expanded for visibility
   - Height-limited with scroll
   - Matches design of other collapsible sections

10. **Page Margins and Styling** ✓
    - ScrollViewer: `Padding="0,0,16,0"`
    - StackPanel: `Spacing="16"`, `Margin="0,0,0,24"`
    - Consistent with all other application pages

**Files Modified**: 8 files (~1000+ lines)
- AssetExtractionPage.xaml (complete redesign)
- AssetExtractionPage.xaml.cs (full refactor with state management)
- AssetExtractionConfig.cs (language filter property)
- AssetTextExtractor.cs (simplified loading + language detection)
- AppConfig.cs (added LevelColor to LogEntry)
- XUnity-LLMTranslatePlus.csproj (embedded resource)
- Resources/classdata.tpk (new embedded file)

**Build Status**: ✅ Successful compilation with minimal warnings

**Impact**: Significantly improved user experience with persistent state, non-blocking operations, and consistent UI design across the application.

---

### 2025-01-27: Critical Bug Fixes for Asset Extraction Translation Save

**Major Bug Fixes** - Resolved critical issues preventing translations from being saved correctly:

**Issue #1: New Translations Not Being Saved (CRITICAL)**

*Problem*:
- Users reported translations were logged as saved but missing from file
- Evidence: Log showed "520 translations saved" but file only contained 338 entries
- **182 translations were lost** completely

*Root Cause*:
- `TextFileParser.SaveFileAsync` only processed lines from `_fileLines` (original file content)
- New translations in `_translations` dictionary but not in `_fileLines` were **silently ignored**
- Method iterated through existing file lines, updated matching keys, but never appended new entries

*Solution* (TextFileParser.cs:273-368):
```csharp
// Step 1: Process existing file lines (update or keep unchanged)
var processedKeys = new HashSet<string>(StringComparer.Ordinal);
foreach (var line in _fileLines)
{
    // ... process and record keys in processedKeys
}

// Step 2: Append NEW translations (critical addition!)
foreach (var kvp in _translations)
{
    if (!processedKeys.Contains(kvp.Key))  // New translation
    {
        string newLine = kvp.Value.Contains(' ') || kvp.Value.Contains('\t')
            ? $"{kvp.Key}=\"{kvp.Value}\""
            : $"{kvp.Key}={kvp.Value}";
        outputLines.Add(newLine);
    }
}
```

**Issue #2: Key-Value Pairs Split Across Lines**

*Problem*:
- Some entries appeared as:
  ```
  これでバックアップデータを読み込む準備はできたわ
  =这样读取备份数据的准备工作就完成了
  ```
- Should be single line: `これでバックアップデータを読み込む準備はできたわ=这样读取备份数据的准备工作就完成了`

*Root Cause*:
- Keys or values contained **actual newline characters** (`\n`)
- `WriteLineAsync` wrote these as real line breaks instead of escaped sequences
- XUnity format requires: `\n` = literal two characters (backslash + n), NOT actual newline

*Solution* (TextFileParser.cs:58-72):

Added escape method:
```csharp
/// <summary>
/// Escape special characters to XUnity file format (literal characters)
/// Converts actual special chars (e.g. newline) to literal escape sequences (e.g. \n as two chars)
/// </summary>
private static string EscapeSpecialChars(string text)
{
    if (string.IsNullOrEmpty(text))
        return text;

    return text
        .Replace("\\", "\\\\")  // Backslash must be first
        .Replace("\n", "\\n")   // Actual newline → literal \n
        .Replace("\r", "\\r")   // Actual carriage return → literal \r
        .Replace("\t", "\\t");  // Actual tab → literal \t
}
```

Applied in both save methods:
- `SaveTranslationsDirectAsync` (line 490-491)
- `SaveFileAsync` (line 357-358)

**Results**:
- ✅ All 520 translations now saved correctly (0 lost)
- ✅ All key-value pairs on single lines
- ✅ Complies with XUnity file format specification
- ✅ Special characters properly escaped

**Additional Improvements from Same Session**:

1. **Removed Redundant Operation Log**
   - Deleted separate log UI from AssetExtractionPage
   - All logs now unified in "Log Monitor" page
   - Simplified UI, reduced code duplication

2. **Simplified Loading UI**
   - Removed full-screen `LoadingOverlay`
   - Replaced with lightweight `ProgressPanel` + `ProgressBar`
   - Non-intrusive progress display

3. **Optimized Concurrency Configuration**
   - Changed from fixed `BatchConcurrency=3` to dynamic calculation
   - Now uses sum of all enabled API endpoints' `MaxConcurrent` (capped at 100)
   - Example: 1 endpoint with MaxConcurrent=64 → uses 64 concurrent tasks
   - Matches `FileMonitorService` behavior for consistency

**Files Modified**: 3 files
- TextFileParser.cs (added EscapeSpecialChars, fixed SaveFileAsync, fixed SaveTranslationsDirectAsync)
- PreTranslationService.cs (optimized concurrency calculation, improved merge logic)
- AssetExtractionPage.xaml + .cs (removed operation log, simplified loading UI)

**Build Status**: ✅ Successful compilation

**Impact**: **Critical bugs resolved** - Asset extraction feature now fully functional with all translations saved correctly and proper file format compliance.
