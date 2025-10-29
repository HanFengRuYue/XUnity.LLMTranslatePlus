# CLAUDE.md

AI coding assistant guidance for XUnity.LLMTranslatePlus codebase.

## Recent Updates

### 2025-10-29: Theme Toggle NavigationViewItem Standardization
- **MainWindow NavigationView**: Refactored theme toggle to use standard NavigationViewItem pattern
  - Removed nested Button/TextBlock structure (non-standard)
  - Changed from `NavigationViewItem > Button > TextBlock` to direct `NavigationViewItem.Content`
  - Event: `ThemeToggleButton_Click` → `ThemeNavItem_Tapped` (requires `using Microsoft.UI.Xaml.Input`)
  - Method: `UpdateThemeButton()` → `UpdateThemeNavItem()`
  - Updates: `ThemeNavItem.Content` (text) + `ThemeNavIcon.Glyph` (icon) + `ToolTipService.ToolTip`
- **Result**: Theme toggle now visually consistent with other NavigationViewItems (e.g., "关于")
- **Behavior**: Retained `SelectsOnInvoked="False"` (no navigation), full theme cycle functionality preserved

### 2025-10-28: DataGrid Column Resizing & Taskbar Progress
- **DataGrid**: Replaced ListView with resizable/sortable DataGrid (CommunityToolkit.WinUI.UI.Controls)
  - Star-sized columns with minimum widths
  - Renamed `ExtractedTextsListView` → `ExtractedTextsDataGrid`
- **Taskbar Progress**: ITaskbarList3 COM interop via P/Invoke
  - Shows real-time progress for scan/extract/translate operations
  - Progress values: UI (0-100) → Taskbar (0.0-1.0)
  - All UI updates via `DispatcherQueue.TryEnqueue()`

### 2025-10-27: Asset Extraction Context Menu & Pattern Management
- **Right-Click Menu**: Copy text/path/file, add field to blacklist, smart regex generation
- **Smart Regex Auto-Generation**: JSON objects/arrays, coordinates, hex strings, UUIDs
- **Exclude Pattern UI**: ListView with add/delete, auto-deduplication

### 2025-10-27: MonoBehaviour Smart Scanning
- **Two Scan Modes**: SpecifiedFields (default) / AllStringFields
- **AllStringFields Mode**: Recursive traversal with smart filtering (8 heuristic rules)
  - MaxRecursionDepth (1-5, default: 3)
  - ExcludeFieldNames blacklist (guid, id, path, url, etc.)
- **IsLikelyGameText()**: Filters GUID, file paths, URLs, hex colors, Base64

### 2025-10-27: Asset Extraction Major Fix
- **File Scanning**: Changed from extension-based to enumerate-all approach
  - NO file size limits, NO extension filtering
  - **CRITICAL**: Level files (level0, level1) have NO extension but contain massive text
- **Field Matching**: TryGetFieldWithVariants() handles Unity `m_` prefix variants
  - Auto-tries: exact → m_prefix → m_Capitalized → remove_m_ → lowercase
  - Default fields include `m_Text`, `m_text` for Unity UI compatibility

### 2025-10-27: UI Optimization
- Table height: 400px → 600px
- Path display: Full path → Relative path (tooltips show full path)
- Fixed CS0168/CS0067 warnings

## Project Overview

**XUnity大语言模型翻译Plus** - .NET 9.0 + WinUI 3 app for AI-powered game text translation with XUnity.AutoTranslator integration.

- **Tech Stack**: C# 13.0, .NET 9.0, WinUI 3, Windows App SDK 1.8
- **Deployment**: Framework-Dependent Single-File (~10MB zip)
- **Build**: `dotnet build` | `.\Build-Release.ps1` (x86/x64/arm64)
- **Critical**: Single-file requires `Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory)` in App.xaml.cs

## Core Architecture

### Async & Threading
- **Locking**: Use `SemaphoreSlim`, NEVER `lock` in async methods
- **Channel<T>**: Initialize BEFORE writes, use `TryWrite()` when closable
- **UI Updates**: ALWAYS use `DispatcherQueue.TryEnqueue()` from non-UI threads
- **File Access**: `FileShare.ReadWrite` for reading XUnity files, `FileShare.Read` for writing

### Performance Patterns
- **HttpClient**: Use `IHttpClientFactory`
- **Regex**: Use `[GeneratedRegex]` for 3-140x performance
- **String Ops**: Prefer `AsSpan()` over `Substring()` in loops
- **Batching**: LogService (50 entries/2s), FileMonitorService (2 translations/0.5s)

### Security
- **Path Validation**: All paths through `PathValidator`
- **API Keys**: Windows DPAPI encrypted with `"ENC:"` prefix

### XUnity File Format
**CRITICAL**: Escape sequences (`\n`, `\r`, `\t`) stored as **literal two characters**.
- Always use `EscapeSpecialChars()` before writing
- Comments start with `//`

## Translation Pipeline

**Order Matters**:
1. Increment `TranslatingCount`
2. **Check exact terminology match FIRST** (before escape processing) → Return if matched
3. Extract special chars → placeholders
4. Build terminology reference + context
5. Call API with retry (exponential backoff for 429, linear for others)
6. Restore special chars
7. Update context cache + smart extraction (background)
8. Decrement `TranslatingCount` (in `finally`)

**Context Cache**: Thread-safe via `_contextLock`, `AddToContext()` requires `AppConfig`

## Unity Asset Extraction

### File Scanning Strategy
**CRITICAL**: NO file size/extension filtering. Enumerate ALL files, skip only obvious non-assets (.dll, .exe, .txt, .json, .mp3, .jpg).
- Level files (level0, level1) have NO extension but contain massive MonoBehaviour text
- Try each file: Bundle → Assets → Skip silently

### MonoBehaviour Field Matching
**CRITICAL**: Unity uses `m_` prefix (m_Text, m_Color). `TryGetFieldWithVariants()` auto-handles:
1. Exact match → 2. Add m_ → 3. Add m_ + capitalize → 4. Remove m_ → 5. Remove m_ + lowercase

### Scan Modes
- **SpecifiedFields** (default): Only scan configured `MonoBehaviourFields`
- **AllStringFields**: Recursive scan with smart filtering
  - `MaxRecursionDepth` (1-5, default: 3)
  - `ExcludeFieldNames` blacklist
  - `IsLikelyGameText()`: 8 heuristic rules (GUID, paths, URLs, hex, Base64, etc.)

### Backend Support
- **Mono/.NET**: MonoCecilTempGenerator + Managed/*.dll
- **IL2CPP**: Cpp2IlTempGenerator + global-metadata.dat + GameAssembly
- **ClassDatabase**: Embedded classdata.tpk → User path fallback

### Critical Details
- `IsDummy` is property (NOT method)
- TextAsset `m_Script` is `byte[]`: Use `AsByteArray` → `Encoding.UTF8.GetString()`
- Use `fileInst.path` for Managed folder path
- Bundle: `LoadBundleFile(path, unpackIfPacked: true)` for LZMA/LZ4

## Dependency Injection

All services are singletons in `App.xaml.cs`. Pages resolve via `App.GetService<LogService>()`.

## Critical Gotchas

1. **WinUI 3 Threading**: UI updates MUST use `DispatcherQueue.TryEnqueue()`
2. **XUnity File Format**: Escape sequences as **literal two characters**. Use `EscapeSpecialChars()`.
3. **SemaphoreSlim Deadlock**: Never call methods acquiring same lock while holding it
4. **Channel<T>**: Initialize BEFORE writes, use `TryWrite()` when closable
5. **FileSystemWatcher Loop**: Disable during writes (300ms delay), re-enable in `finally`
6. **Translation Order**: Terminology matching BEFORE escape character processing
7. **Rate Limiting**: HTTP 429 exponential backoff, does NOT count toward error threshold
8. **SaveFileAsync**: Append new translations from `_translations` not in `_fileLines` (use `HashSet<string>`)
9. **ConfigService**: New `AppConfig` fields must be added to `configToSave` copy
10. **Page State**: Use `NavigationCacheMode.Required` for state preservation (AssetExtractionPage)
11. **Unity Asset Path**: Use `fileInst.path` property, NOT string parameter
12. **Third-Party Exceptions**: Use `[DebuggerNonUserCode]` + `[DebuggerStepThrough]` for wrappers
13. **Async All The Way**: Never `.Result` or `.Wait()`. Always propagate `CancellationToken`.
14. **Asset File Scanning**: NO size/extension filtering. Level files have NO extension.
15. **Asset Field Names**: Unity uses `m_` prefix. Missing `m_Text` = missing 90%+ text.
16. **Asset Exception Handling**: Non-asset files throw exceptions - EXPECTED. Log at Debug level.

## UI Patterns

- **Title Bar Order**: `this.Title` → `AppWindow.SetIcon()` → `ExtendsContentIntoTitleBar` → `SetTitleBar()`
- **Auto-save**: ApiConfigPage/TerminologyPage (800ms), TranslationSettingsPage (500ms)
- **GetConfigFromUIAsync**: Load fresh config FIRST to preserve other pages' settings

## Exception Hierarchy

- `TranslationException`
  - `ApiConnectionException` (Network/HTTP)
    - `RateLimitException` (HTTP 429, includes `RetryAfterSeconds`)
  - `ApiResponseException` (API parsing)
  - `TranslationTimeoutException`
  - `ConfigurationValidationException`
  - `FileOperationException`

**Best Practice**: Catch specific types first. `RateLimitException` triggers exponential backoff, NOT counted toward error threshold.
