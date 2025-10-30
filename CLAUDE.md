# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Recent Updates

### 2025-10-30: File I/O Performance Optimization
- **I/O Retry Mechanism**: Reduced from 5 retries to 3 retries with exponential backoff
  - Read retry delays: 50ms, 100ms, 200ms (was random 100-500ms)
  - Write retry delays: 100ms, 200ms, 400ms (was random 200-1000ms)
  - Result: 85-90% reduction in worst-case latency (from ~10s to ~1.5s)
- **Fixed Delays Optimization**: Reduced multiple fixed delays in FileMonitorService
  - File change detection: 200ms → 100ms (line 369)
  - Batch write check interval: 200ms → 100ms (line 691)
  - Post-write stabilization: 300ms → 200ms (line 740)
- **Impact**: Text read-to-write cycle now completes in <1 second (excluding API translation time)

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
- **Deployment**: Framework-Dependent Single-File (~10MB zip, ~40MB exe)
- **Platforms**: x86, x64, ARM64
- **Requires**: .NET 9 Desktop Runtime + Windows App SDK 1.8 Runtime
- **Build**: `dotnet build` | `.\Build-Release.ps1` (builds all architectures + 7-Zip packaging)
- **Critical**: Single-file requires `Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory)` in App.xaml.cs

## Build Commands

### Development
```bash
# Build for development
dotnet build

# Run directly
dotnet run --project XUnity-LLMTranslatePlus/XUnity-LLMTranslatePlus.csproj
```

### Release (Multi-Architecture)
```powershell
# Build all architectures with 7-Zip packaging (recommended)
.\Build-Release.ps1

# Skip clean step for faster incremental builds
.\Build-Release.ps1 -SkipClean

# Manual single-architecture publish
dotnet publish --configuration Release --runtime win-x64 --self-contained false /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

**Build Script Features** (Build-Release.ps1):
- Verifies .NET 9 SDK and 7-Zip availability
- Builds x86, x64, ARM64 in parallel
- Outputs to `Release/[runtime]/` with ZIP packaging
- Displays size summary and compression ratios

**Requirements**: 7-Zip at `C:\Program Files\7-Zip\7z.exe`

### Testing
No automated test suite currently exists in the codebase.

## Core Architecture

### Dependency Injection
All services registered as **singletons** in `App.xaml.cs` using `Microsoft.Extensions.DependencyInjection`:
```csharp
services.AddHttpClient();
services.AddSingleton<LogService>();
services.AddSingleton<ConfigService>();
services.AddSingleton<TerminologyService>();
services.AddSingleton<ApiClient>();
services.AddSingleton<ApiPoolManager>();
services.AddSingleton<TranslationService>();
services.AddSingleton<FileMonitorService>();
// ... and more
```

**Access Pattern**: Pages resolve services via `App.GetService<ServiceType>()`

### Service Layer Architecture

**Key Services and Responsibilities**:

1. **ConfigService** (Foundation Layer)
   - Persists config to `%APPDATA%\XUnity-LLMTranslatePlus\config.json`
   - DPAPI encryption for API keys (prefix: `"ENC:"`)
   - Thread-safe via `SemaphoreSlim`
   - Emits `ConfigChanged` events with property tracking
   - **Critical**: When adding new AppConfig fields, include in `configToSave` copy

2. **LogService** (Infrastructure Layer)
   - Background `Channel<LogEntry>` consumer
   - Batch writes (50 entries or 2s timeout)
   - In-memory cache (max 1000) + ObservableCollection for UI
   - Daily log files in AppData

3. **ApiClient + ApiPoolManager** (API Layer)
   - `ApiClient`: OpenAI-compatible HTTP client using `IHttpClientFactory`
   - `ApiPoolManager`: Multi-endpoint load balancing with per-endpoint `SemaphoreSlim`
   - Tracks `ApiEndpointStats` (success/failure, performance)
   - Dynamic weight-based endpoint selection

4. **TranslationService** (Core Business Logic)
   - Orchestrates translation pipeline (see Translation Pipeline section)
   - Context cache management (thread-safe via `_contextLock`)
   - Tracks: `TotalTranslated`, `TotalFailed`, `TotalSkipped`
   - Emits `ProgressUpdated` events

5. **FileMonitorService** (File Monitoring)
   - Monitors XUnity translation file via `FileSystemWatcher`
   - High-performance: `Channel<T>` for pending texts, multi-consumer tasks (dynamic count based on endpoint concurrency)
   - Batch writes via separate channel (2 translations per 0.5s)
   - Text aggregation for "typewriter effect" (configurable 1-10s delay)
   - Processed text deduplication via `HashSet<string>`

6. **TerminologyService + SmartTerminologyService** (Terminology Layer)
   - Loads/saves terminology to JSON in `%APPDATA%\XUnity-LLMTranslatePlus\terminology\`
   - Supports multiple databases with switching
   - Line-based search and fuzzy matching

7. **AssetScannerService + AssetTextExtractor** (Unity Asset Layer)
   - Scanner: Discovers asset files (`.assets`, `.unity3d`, `.bundle`) using AssetsTools.NET
   - Extractor: Extracts text from MonoBehaviour/TextAsset with smart filtering
   - Supports Mono/.NET and IL2CPP backends
   - Field matching with `m_` prefix variants (see Unity Asset Extraction section)

8. **TaskbarProgressService** (Windows Integration)
   - COM interop (`ITaskbarList3`) via P/Invoke
   - Shows scan/extract/translate progress on taskbar
   - UI updates via `DispatcherQueue.TryEnqueue()`

### Async & Threading
- **Locking**: Use `SemaphoreSlim`, NEVER `lock` in async methods
- **Channel<T>**: Initialize BEFORE writes, use `TryWrite()` when closable
  - Unbounded channels with single-reader optimization for performance
- **UI Updates**: ALWAYS use `DispatcherQueue.TryEnqueue()` from non-UI threads
- **File Access**: `FileShare.ReadWrite` for reading XUnity files, `FileShare.Read` for writing
- **I/O Retry**: Exponential backoff strategy (3 retries max)
  - Read: 50ms, 100ms, 200ms
  - Write: 100ms, 200ms, 400ms

### Performance Patterns
- **HttpClient**: Use `IHttpClientFactory` for connection pooling
- **Regex**: Use `[GeneratedRegex]` for 3-140x performance (.NET 7+)
- **String Ops**: Prefer `AsSpan()` over `Substring()` in hot paths
- **Batching**:
  - LogService: 50 entries or 2s timeout
  - FileMonitorService: 2 translations or 0.5s timeout
  - Reduces file I/O contention and improves throughput

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

## Event-Driven Communication

Services emit events for decoupled communication:
- **ConfigService**: `ConfigChanged(HashSet<string> changedProperties)` - tracks which config fields changed
- **TranslationService**: `ProgressUpdated(TranslationProgressEventArgs)` - translation metrics updates
- **FileMonitorService**: `StatusChanged`, `EntryTranslated`, `ErrorThresholdReached` - monitoring state
- **LogService**: `LogAdded(LogEntry)` - new log entries for UI binding
- **ApiPoolManager**: `StatsUpdated(ApiEndpointStats)` - endpoint performance metrics

## Key NuGet Dependencies

- `Microsoft.Extensions.DependencyInjection` (9.0.10) - DI container
- `Microsoft.Extensions.Http` (9.0.10) - HttpClientFactory
- `AssetsTools.NET` (3.0.2) - Unity asset parsing
- `AssetsTools.NET.Cpp2IL` (3.0.2) - IL2CPP support
- `AssetsTools.NET.MonoCecil` (3.0.2) - Mono/.NET support
- `CommunityToolkit.WinUI.UI.Controls.DataGrid` (7.1.2) - DataGrid component
- `System.Security.Cryptography.ProtectedData` (9.0.10) - DPAPI encryption

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

### WinUI 3 Navigation
- **NavigationView-based**: MainWindow hosts NavigationView with page navigation
- **Tag-based routing**: NavigationViewItems use Tag property for page type mapping
- **Page caching**: Use `NavigationCacheMode.Required` for state preservation (e.g., AssetExtractionPage)
- **Static caches**: HomePage uses static `_recentTranslationsCache` for persistence across navigation

### Window Initialization
- **Title Bar Order** (IMPORTANT):
  1. `this.Title = "..."`
  2. `AppWindow.SetIcon(iconPath)`
  3. `ExtendsContentIntoTitleBar = true`
  4. `SetTitleBar(customTitleBar)`

### Configuration Auto-save
- **Debounced saves**: Prevent excessive writes during rapid UI changes
  - ApiConfigPage/TerminologyPage: 800ms debounce
  - TranslationSettingsPage: 500ms debounce
- **GetConfigFromUIAsync pattern**: Load fresh config FIRST, then update only relevant fields
  - Preserves settings from other pages not currently in UI

### Theme Support
- **Theme toggle**: Cycles Light → Dark → Default (system)
- **Implementation**: NavigationViewItem with `SelectsOnInvoked="False"` in footer
- **Persistence**: Saved to `AppConfig.ApplicationTheme`
- **Update method**: `UpdateThemeNavItem()` updates icon, text, and tooltip

## Exception Hierarchy

- `TranslationException`
  - `ApiConnectionException` (Network/HTTP)
    - `RateLimitException` (HTTP 429, includes `RetryAfterSeconds`)
  - `ApiResponseException` (API parsing)
  - `TranslationTimeoutException`
  - `ConfigurationValidationException`
  - `FileOperationException`

**Best Practice**: Catch specific types first. `RateLimitException` triggers exponential backoff, NOT counted toward error threshold.

## Directory Structure

```
XUnity-LLMTranslatePlus/
├── App.xaml(.cs)                     # Application entry point, DI setup
├── MainWindow.xaml(.cs)              # Main window with NavigationView shell
├── Services/                         # Core service layer (all singletons)
│   ├── ConfigService.cs              # Config persistence + DPAPI encryption
│   ├── LogService.cs                 # Async batched logging
│   ├── ApiClient.cs                  # OpenAI-compatible HTTP client
│   ├── ApiPoolManager.cs             # Multi-endpoint load balancing
│   ├── TranslationService.cs         # Translation orchestration
│   ├── FileMonitorService.cs         # XUnity file monitoring + batching
│   ├── TerminologyService.cs         # Term database management
│   ├── SmartTerminologyService.cs    # Fuzzy term matching
│   ├── AssetScannerService.cs        # Unity asset file discovery
│   ├── AssetTextExtractor.cs         # MonoBehaviour/TextAsset extraction
│   ├── TaskbarProgressService.cs     # Windows taskbar progress (COM)
│   └── ...
├── Views/                            # WinUI 3 Pages
│   ├── HomePage.xaml(.cs)            # Translation monitoring dashboard
│   ├── ApiConfigPage.xaml(.cs)       # API endpoint configuration
│   ├── TranslationSettingsPage.xaml(.cs)  # Translation settings
│   ├── TerminologyPage.xaml(.cs)     # Term database UI
│   ├── AssetExtractionPage.xaml(.cs) # Unity asset extraction UI
│   ├── TextEditorPage.xaml(.cs)      # Live translation file editor
│   ├── LogPage.xaml(.cs)             # Real-time log viewer
│   └── ...
├── Models/                           # Data models
│   ├── AppConfig.cs                  # Main configuration model
│   ├── ApiEndpoint.cs                # API endpoint config
│   ├── AssetExtractionConfig.cs      # Asset extraction settings
│   └── ...
├── Utils/                            # Helper utilities
│   ├── EscapeCharacterHandler.cs     # Special char processing
│   ├── TextFileParser.cs             # XUnity file parser
│   ├── PathValidator.cs              # Security path validation
│   └── SecureDataProtection.cs       # DPAPI wrapper
├── Exceptions/                       # Custom exception hierarchy
│   └── TranslationException.cs       # Base + derived exceptions
└── Resources/                        # Embedded assets
    └── classdata.tpk                 # Unity type database

Build Scripts:
├── Build-Release.ps1                 # Multi-architecture build + packaging
└── XUnity-LLMTranslatePlus.sln       # Visual Studio solution
```

**Entry Points**:
- `App.xaml.cs`: DI container setup, service registration
- `MainWindow.xaml.cs`: NavigationView shell, theme management
- `HomePage`: Default landing page on startup

**Configuration Storage**: `%APPDATA%\XUnity-LLMTranslatePlus\`
- `config.json` - Main app configuration
- `terminology/` - Term database files
- `logs/` - Daily log files
