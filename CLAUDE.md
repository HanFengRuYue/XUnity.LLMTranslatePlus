# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Recent Updates

### 2025-10-27: Asset Extraction Context Menu & Pattern Management
**Improvement**: Added comprehensive right-click menu and exclude pattern management for extraction results.

**Problem Identified**:
- After scanning, users found many technical data (JSON, coordinates, etc.) but lacked easy filtering options
- Users couldn't copy text from extraction results table
- Auto-generated exclude patterns had no frontend management interface

**Solutions Implemented**:
- **Right-Click Menu** (AssetExtractionPage.xaml:444-481):
  - 📋 **Copy Original Text** - One-click copy to clipboard
  - 📄 **Copy Field Path** - Copy complete field path (e.g., "data.Array.dialogText")
  - 📁 **Copy Source File** - Copy asset file full path
  - ➕ **Add Field Name to Blacklist** - Auto-extract field name to ExcludeFieldNames
  - 🚫 **Add to Exclude Pattern** - Smart regex generation to ExcludePatterns

- **Exclude Pattern Management UI** (AssetExtractionPage.xaml:252-311):
  - New ListView in "Advanced Filter Settings" to display all exclude patterns
  - Add/Delete buttons with multi-select support
  - Uses Consolas monospace font for regex readability
  - Real-time count display: "共 N 个模式"
  - InfoBar hint: users can quickly add patterns via right-click

- **Smart Regex Generation** (AssetExtractionPage.xaml.cs:786-814):
  - JSON object → `^\s*\{.*\}\s*$`
  - JSON array → `^\s*\[.*\]\s*$`
  - Numeric coordinates → `^[\d\.\-,\s]+$`
  - Hex strings (≥8 chars) → `^[0-9a-fA-F]{8,}$`
  - UUID/GUID → `^[0-9a-fA-F-]+$`
  - Long text → `^<first 20 chars>.*$`

- **Configuration Sync**:
  - LoadConfigToUIAsync loads patterns to UI (AssetExtractionPage.xaml.cs:136-143)
  - AutoSaveConfigAsync saves patterns to config (AssetExtractionPage.xaml.cs:197-202)
  - Right-click menu adds directly to UI collection for instant feedback
  - Auto-deduplication and empty pattern filtering

- **Text Selection Removal**:
  - Changed "Original Text" column from read-only TextBox back to TextBlock
  - Unified user interaction: all copying via right-click menu only

**User Workflow**:
1. Scan completes → find technical data
2. Right-click → "Add to Exclude Pattern"
3. System auto-generates appropriate regex
4. Expand "Advanced Filter Settings" → view/edit/delete in pattern list
5. Next scan → data automatically filtered

**Files Modified**:
- `Views/AssetExtractionPage.xaml` - Added context menu, pattern management UI
- `Views/AssetExtractionPage.xaml.cs` - Added event handlers, PatternEntry class
- Pattern management fully integrated with existing auto-save system

### 2025-10-27: MonoBehaviour All String Fields Smart Scanning
**Feature**: Added intelligent scanning of all string fields in MonoBehaviour without field name restrictions.

**Problem**: Users could miss game text if developers used custom field names not in the predefined list.

**Solution**: Two-mode scanning system with smart filtering.

**Implementation**:
- **Data Model** (AssetExtractionConfig.cs:6-65):
  - Added `MonoBehaviourScanMode` enum (SpecifiedFields / AllStringFields)
  - Added `ScanMode` property (default: SpecifiedFields for backward compatibility)
  - Added `MaxRecursionDepth` property (default: 3, range: 1-5)
  - Added `ExcludeFieldNames` list with common technical field names (guid, id, path, url, shader, material, texture, etc.)

- **Core Extraction Logic** (AssetTextExtractor.cs:793-933):
  - `ExtractAllStringFieldsRecursive()`: Recursive traversal using `.Children` property
    - Limits recursion depth to avoid performance issues
    - Records full field path (e.g., "parent.child.text")
    - Only extracts `TypeName == "string"` fields
  - `IsLikelyGameText()`: Smart heuristic filtering with 8 rules:
    1. Field name blacklist check (case-insensitive)
    2. GUID format detection (standard and Unity GUID)
    3. File path detection (Assets/, .prefab, .png, etc.)
    4. URL/URI detection
    5. Pure numeric detection
    6. Variable name format (e.g., MAX_COUNT)
    7. Hex color codes (#FFF, #FFFFFF)
    8. Base64 encoding detection
  - Modified `ExtractFromAssetsFileAsync()`: Branches based on ScanMode
    - Records detailed statistics in AllStringFields mode

- **UI Updates** (AssetExtractionPage.xaml:87-312):
  - Added "MonoBehaviour Scan Mode" radio button group
  - "Specified Fields" mode: Shows field name management Expander
  - "All String Fields" mode: Shows advanced filter settings Expander
    - Recursion depth slider (1-5 layers)
    - Field name blacklist management (similar to field name list UI)
  - Dynamic UI visibility based on selected mode

- **Backend Logic** (AssetExtractionPage.xaml.cs):
  - Added `_excludeFieldNames` ObservableCollection
  - `ScanModeRadioButtons_SelectionChanged`: Toggles UI visibility
  - `UpdateScanModeUI()`: Controls Expander visibility
  - Advanced filter event handlers (add/delete/selection/text changed)
  - Config loading and saving for all new properties

**Results**:
- Users can now extract text without knowing exact field names
- Smart filtering reduces false positives (technical data extraction)
- Detailed Debug logs show: total fields scanned, heuristic filters applied, final text count
- Fully backward compatible - defaults to original SpecifiedFields mode

**Testing**:
- ✅ Compiled successfully with 0 warnings, 0 errors
- ✅ Mode switching correctly shows/hides UI sections
- ✅ Recursion depth slider updates properly
- ✅ Config auto-saves and persists correctly

### 2025-10-27: Asset Extraction Major Fix
**Problem**: Asset extraction was only finding 3 texts from 27 files, missing 35+ level files and all MonoBehaviour text.

**Root Causes**:
1. Extension-based filtering skipped extensionless level files (level0, level1, etc.)
2. MonoBehaviourFields config only had `text`, but Unity uses `m_Text` (with `m_` prefix)
3. Field name mismatch = 0 texts extracted from all assets

**Solutions Implemented**:
- **AssetScannerService**: Changed from extension-based to enumerate-all-files approach
  - Only excludes obvious non-assets (.dll, .exe, .txt, .json, .mp3, .jpg, etc.)
  - NO file size limits (some assets are huge, some are tiny)
  - Tries loading EVERY candidate file (Bundle → Assets → Skip)

- **AssetExtractionConfig**: Added Unity standard field names
  - Added `m_Text`, `m_text` to default MonoBehaviourFields
  - Expanded field list with more common names (dialogue, story, hint, tip)

- **AssetTextExtractor**: Flexible field matching with `TryGetFieldWithVariants()`
  - Auto-tries: exact → m_prefix → m_Capitalized → remove_m_ → lowercase
  - Users can now config ANY variant (text/m_Text/m_text) - all work
  - Added Debug-level logging for MonoBehaviour scan statistics

**Results**:
- Scan now finds 114 files (was 27) - includes all level files
- Extraction increased from 3 texts to hundreds/thousands per game
- Users no longer need to manually add `m_` prefix to field names

### 2025-10-27: Asset Extraction UI Optimization
**Improvements**: Enhanced user experience on asset extraction page to display more information with cleaner layout.

**Changes Implemented**:
- **Table Height Increase**:
  - Increased extracted text table height from 400 to 600 pixels
  - Users can now view 50% more text entries without scrolling

- **Path Display Simplification**:
  - **ExtractedTextEntry Model**: Added `RelativeSourcePath` property for UI display
  - **AssetTextExtractor**: Added `GetRelativePath()` helper method using `Path.GetRelativePath()`
  - **UI Display**: Shows relative paths (e.g., `GameData\level0`) instead of full paths (e.g., `D:\Games\MyGame\GameData\level0`)
  - **Tooltip**: Hovering over paths still shows full path for reference
  - **Search**: Updated to search in relative paths for better user experience

- **Code Quality**:
  - Fixed CS0168 warning: Removed unused exception variable in `AssetTextExtractor.cs`
  - Fixed CS0067 warning: Removed unused `INotifyPropertyChanged` interface from `AssetExtractionPage`

**Files Modified**:
- `Models/AssetExtractionConfig.cs` - Added `RelativeSourcePath` property
- `Services/AssetTextExtractor.cs` - Added relative path calculation in 3 locations (TextAsset, MonoBehaviour, GameObject)
- `Views/AssetExtractionPage.xaml` - Increased height, changed binding to `RelativeSourcePath`
- `Views/AssetExtractionPage.xaml.cs` - Updated search logic for relative paths

**Benefits**:
- Cleaner UI with shorter, more readable file paths
- Better use of screen space with taller table
- Reduced visual clutter while maintaining full path information via tooltips
- Zero compilation warnings

## Project Overview

**XUnity大语言模型翻译Plus** - WinUI 3 application integrating with XUnity.AutoTranslator for AI-powered game text translation.

- **Framework**: .NET 9.0 + WinUI 3
- **Deployment**: Framework-Dependent Single-File (~40MB exe → ~10MB zip)
- **Runtime**: .NET 9 Desktop Runtime + Windows App SDK 1.8 Runtime
- **Language**: C# 13.0 with nullable reference types

## Build Commands

```bash
# Development
dotnet build
dotnet run --project XUnity-LLMTranslatePlus/XUnity-LLMTranslatePlus.csproj

# Release (builds x86/x64/arm64 + ZIP archives)
.\Build-Release.ps1

# Manual single-file publish
dotnet publish --configuration Release --runtime win-x64 --self-contained false /p:PublishSingleFile=true
```

**Critical**: Single-file deployment requires environment variable in `App.xaml.cs`:
```csharp
Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
```

## Core Architecture Patterns

### 1. Async Locking
- **Never** use `lock` in async methods - use `SemaphoreSlim`
- **Never** call methods acquiring the same lock while holding it

```csharp
await _semaphore.WaitAsync(cancellationToken);
try { /* critical section */ }
finally { _semaphore.Release(); }
```

### 2. High-Performance Batching with Channel<T>
- **LogService**: Batches log writes (50 entries or 2s)
- **FileMonitorService**: Batches file writes (2 translations or 0.5s)

**Critical Patterns**:
- Initialize Channel BEFORE any code writes to it
- Use `TryWrite()` instead of `WriteAsync()` when Channel may be closed
- Disable FileSystemWatcher during writes (300ms delay) to prevent loops

### 3. Thread-Safe Context Cache
- Context stores last N translations (default 10, configurable 1-100)
- All access protected by `_contextLock`
- Always pass `AppConfig` to `AddToContext()` - cache size is dynamic

### 4. Concurrent File Access
**Critical**: Use `FileShare.ReadWrite` when reading files XUnity may write to:

```csharp
await using var fileStream = new FileStream(
    path, FileMode.Open, FileAccess.Read,
    FileShare.ReadWrite,  // Allows XUnity to write simultaneously
    4096, FileOptions.Asynchronous);
```

### 5. Security & Performance
- **HttpClient**: Always use `IHttpClientFactory`
- **Path Validation**: All paths MUST go through `PathValidator`
- **API Keys**: Encrypted with Windows DPAPI, `"ENC:"` prefix
- **Regex**: Use `[GeneratedRegex]` for 3-140x performance
- **String Ops**: Prefer `AsSpan()` over `Substring()` in loops

## XUnity Translation File Format

**Critical**: Escape sequences (`\n`, `\r`, `\t`) are stored as **literal two characters**, NOT actual newlines.

```
// Comments start with //
key1=value1
key2="value with spaces"
```

**Escaping Required**: Use `EscapeSpecialChars()` before writing:
- Actual `\n` → Literal `\n` (two characters)
- Actual `\t` → Literal `\t` (two characters)

## Translation Pipeline

**CRITICAL**: Terminology matching happens FIRST using original text (before escape processing).

1. Increment `TranslatingCount`
2. **Check exact terminology match** → Return immediately if matched
3. Extract special characters → placeholders (only if step 2 fails)
4. Build terminology reference + context
5. Call API with retry + exponential backoff
6. Restore special characters
7. Update context cache + smart extraction (background)
8. Decrement `TranslatingCount` (in `finally`)

### API Retry Strategy
- **Rate Limiting (429)**: Exponential backoff (2s→4s→8s→16s, ±20% jitter), does NOT count toward error threshold
- **Other Errors**: Linear backoff (2s→4s→6s), counts toward `ErrorThreshold`

## File Monitoring Pipeline

```
File change → 200ms delay → Parse → Queue → Consumer → API → Batch write
```

**Concurrency**: Default 3, configurable 1-100

## Terminology Management

**Multi-File Architecture**:
```
AppData/XUnity-LLMTranslatePlus/
├── terms.csv                    # Default
├── config.json
└── Terminologies/               # Additional files
    ├── game-specific.csv
    └── custom.csv
```

**Key Points**:
- Thread-safe with `_lockObject`
- Current file tracked in `AppConfig.CurrentTerminologyFile`
- Smart extraction runs async in background, saves to current file
- Apply order: Priority DESC → Length DESC

## Unity Asset Extraction

**Architecture**: Pre-extract static texts from Unity assets + Real-time monitoring

**Workflow**:
1. Scan → **Enumerate ALL files** in game directory (skip only obvious non-assets)
2. Extract → TextAsset, MonoBehaviour fields, GameObject names
3. Filter → CJK detection, regex exclusions
4. Translate batch → Use existing TranslationService
5. Merge → Combine with XUnity translation file

**File Scanning Strategy** (Updated 2025-10-27):
- **NO file size limits** - Asset files can be any size
- **NO extension-based filtering** - Scan ALL files including extensionless ones
- **Exclusion list**: Only skip obvious non-assets (.dll, .exe, .txt, .xml, .json, .mp3, .jpg, etc.)
- **Critical**: Level files (`level0`, `level1`, etc.) have NO extension but contain massive amounts of MonoBehaviour text
- Candidates are tried as Bundle first, then as regular assets file, failures are silently skipped

**Backend Support**:
- **Mono/.NET**: MonoCecilTempGenerator with Managed/*.dll
- **IL2CPP**: Cpp2IlTempGenerator with global-metadata.dat + GameAssembly
- **Auto-detection**: Mono → IL2CPP fallback

**Configuration** (Updated 2025-10-27):
- `ScanTextAssets` / `ScanMonoBehaviours` / `ScanGameObjectNames`
- **`ScanMode`**: SpecifiedFields (default) / AllStringFields
  - **SpecifiedFields**: Only scan configured field names in `MonoBehaviourFields`
  - **AllStringFields**: Recursively scan all string fields with smart filtering
- `MonoBehaviourFields`: **MUST include Unity standard fields with `m_` prefix** (e.g., `m_Text` for UI Text component)
  - Only used in SpecifiedFields mode
- **`MaxRecursionDepth`**: Recursion depth limit for AllStringFields mode (1-5, default: 3)
- **`ExcludeFieldNames`**: Field name blacklist for AllStringFields mode (e.g., guid, id, path, url)
  - Case-insensitive matching
  - Applied before other filters
- `SourceLanguageFilter`: All/CJK/Chinese/Japanese/English/Korean/Russian
- `ExcludePatterns`: Regex to filter text content (applies to all modes)
  - Managed via frontend UI (add/edit/delete)
  - Smart auto-generation via right-click menu
- `ClassDatabasePath`: Optional custom classdata.tpk (embedded by default)

**Critical Details**:
- `IsDummy` is property: `field.IsDummy` (not method)
- ClassDatabase: Uses embedded `Resources/classdata.tpk` → User path fallback
- TextAsset `m_Script` is **byte[]**: `AsByteArray` then `Encoding.UTF8.GetString()`
- Use `fileInst.path` NOT string parameter for Managed folder path
- Bundle extraction: `LoadBundleFile(path, unpackIfPacked: true)` for LZMA/LZ4

**MonoBehaviour Field Matching** (Critical - Updated 2025-10-27):
- Unity components use `m_` prefix (e.g., `m_Text`, `m_DialogText`)
- Custom scripts may omit prefix (e.g., `text`, `dialogText`)
- `TryGetFieldWithVariants()` automatically tries multiple naming conventions:
  1. Exact match (`text`)
  2. Add `m_` prefix (`m_text`)
  3. Add `m_` + capitalize (`m_Text`)
  4. Remove `m_` prefix (if present)
  5. Remove `m_` + lowercase first char
- **Default fields** in config now include `m_Text`, `m_text` for Unity UI compatibility
- Users can configure ANY field name variant - the extractor handles all cases automatically

## Dependency Injection

All services are singletons in `App.xaml.cs`:
```csharp
services.AddHttpClient();
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

## UI Patterns

### Custom Title Bar
```csharp
// ORDER MATTERS
this.Title = "XUnity大语言模型翻译Plus";
AppWindow.SetIcon("ICON.ico");
ExtendsContentIntoTitleBar = true;
SetTitleBar(AppTitleBar);
```

### Event-Driven Updates
Always use `DispatcherQueue.TryEnqueue()` for UI updates from non-UI threads.

### Auto-save Debouncing
- ApiConfigPage / TerminologyPage: 800ms
- TranslationSettingsPage: 500ms

**Critical**: `GetConfigFromUIAsync()` must load fresh config first to preserve other pages' settings, then update only relevant fields.

### Pages
- HomePage - Status, start/stop monitoring
- ApiConfigPage - API config, connection test
- TranslationSettingsPage - Game directory, language, context, system prompt
- TerminologyPage - Terminology CRUD, file management, smart extraction toggle
- TextEditorPage - Manual editing, CSV import/export
- AssetExtractionPage - Unity asset scanning, text extraction, selective translation
- LogPage - Real-time logs with filtering
- AboutPage - App info, reset config

## Critical Gotchas

1. **WinUI 3 Threading**: UI updates MUST use `DispatcherQueue.TryEnqueue()`

2. **XUnity File Format**: Escape sequences stored as **literal two characters**. Always use `EscapeSpecialChars()` before writing.

3. **SemaphoreSlim Deadlock**: Never call methods acquiring same lock while holding it.

4. **Channel<T>**: Use `TryWrite()` when Channel may be closed (shutdown). Initialize BEFORE any writes.

5. **FileSystemWatcher Loop**: Disable `EnableRaisingEvents` during writes, 300ms delay, re-enable in `finally`.

6. **File Locking**: Use `FileShare.ReadWrite` for reading, `FileShare.Read` for writing.

7. **Context Cache**: Protected by `_contextLock`. `AddToContext()` requires `AppConfig` parameter.

8. **Translation Pipeline Order**: Terminology matching MUST happen BEFORE escape character processing.

9. **Rate Limiting**: HTTP 429 uses exponential backoff, does NOT count toward error threshold.

10. **SaveFileAsync**: MUST append new translations from `_translations` dictionary not in `_fileLines`. Use `HashSet<string>` to track processed keys.

11. **ConfigService.SaveConfigAsync**: When adding fields to `AppConfig`, add them to `configToSave` copy or they'll reset on save.

12. **Page State Persistence**: Use `NavigationCacheMode.Required` for pages needing state preservation (AssetExtractionPage).

13. **Unity Asset Path**: Use `fileInst.path` property, NOT string parameter passed to loading methods.

14. **Third-Party Exceptions**: Use `[DebuggerNonUserCode]` + `[DebuggerStepThrough]` for wrapper methods to reduce debug interruptions.

15. **Async All The Way**: Never `.Result` or `.Wait()`. Always propagate `CancellationToken`.

16. **Asset Extraction - File Scanning**: NEVER filter by file size or use extension-only filtering. Level files (level0, level1, etc.) have NO extension but contain critical MonoBehaviour data. Always enumerate ALL files and try loading each one.

17. **Asset Extraction - Field Names**: Unity UI components use `m_` prefix (m_Text, m_Color, etc.). ALWAYS include prefixed variants in `MonoBehaviourFields` config OR rely on `TryGetFieldWithVariants()` to auto-match. Missing `m_Text` = missing 90%+ of text.

18. **Asset Extraction - Exception Handling**: Non-asset files will throw exceptions when loaded - this is EXPECTED. Log at Debug level, not Warning/Error. Use try-catch for EACH file, never let one failure abort the entire scan.

## Performance Notes

- **Concurrent Limits**: Default 3, max 100
- **Log Verbosity**: Debug level generates significant I/O
- **Memory**: TextFileParser keeps file in memory
- **UI**: LogPage shows 500 recent entries, HomePage shows 8 recent translations

## Exception Hierarchy

- `TranslationException` - Base
  - `ApiConnectionException` - Network/HTTP errors
    - `RateLimitException` - HTTP 429 (includes `RetryAfterSeconds`)
  - `ApiResponseException` - API parsing errors
  - `TranslationTimeoutException` - Timeout errors
  - `ConfigurationValidationException` - Config validation
  - `FileOperationException` - File I/O errors

**Best Practices**:
- Catch specific types first, then base types
- `RateLimitException` triggers exponential backoff, NOT counted toward error threshold
- Never swallow exceptions without logging
