# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**XUnity大语言模型翻译Plus** - A WinUI 3 application that integrates with XUnity.AutoTranslator to provide AI-powered game text translation using Large Language Models.

- **Framework**: .NET 9.0 + WinUI 3
- **Deployment**: Framework-Dependent Single-File (WindowsAppSDKSelfContained=false, SelfContained=false)
- **Runtime Requirements**: .NET 9 Desktop Runtime + Windows App SDK 1.8 Runtime
- **Target**: Windows 10.0.26100.0 (Min: 10.0.17763.0)
- **Language**: C# 13.0 with nullable reference types enabled

## Build and Development Commands

### Building the Project
```bash
# Build for Debug (default platform: ARM64 first, then x64, x86)
dotnet build

# Build for specific platform
dotnet build --configuration Debug --runtime win-x64
dotnet build --configuration Release --runtime win-x64

# Clean build
dotnet clean && dotnet build
```

### Running the Application
```bash
# Run directly (will build if needed)
dotnet run --project XUnity-LLMTranslatePlus/XUnity-LLMTranslatePlus.csproj

# Run specific configuration
dotnet run --configuration Release
```

### Publishing
```bash
# Framework-dependent single-file publish (recommended)
dotnet publish --configuration Release --runtime win-x64 --self-contained false /p:PublishSingleFile=true

# One-click Release build script (recommended)
.\Build-Release.ps1

# Build for different runtime
.\Build-Release.ps1 -Runtime win-arm64

# Skip clean step for faster builds
.\Build-Release.ps1 -SkipClean
```

**Important Notes**:
- This project uses **framework-dependent deployment** - users must install .NET 9 Desktop Runtime and Windows App SDK 1.8 Runtime. See `RUNTIME-INSTALL.md` for installation instructions.
- The project uses WinUI 3 in Unpackaged mode, so MSIX packaging is disabled. The executable runs directly without requiring installation.
- **Single-file deployment**: Outputs a single .exe file (~40MB) that extracts dependencies at runtime. Requires environment variable setup in `App.xaml.cs`.
- **PublishTrimmed is DISABLED** - IL trimming is not supported by WinUI 3/Windows App SDK (GitHub issue #2478). Setting it has no effect.
- **ReadyToRun is DISABLED** for Release builds to reduce executable size by 20-40%. Trade-off: ~100-200ms slower startup.
- **PDB generation is DISABLED** for Release builds (`DebugType=none`) to eliminate debug symbols (~5-20MB savings).
- The `Build-Release.ps1` script automates the optimized release build process.

## Recent Optimizations (Important Context)

The codebase has undergone comprehensive optimization. Understanding these patterns is crucial:

### 1. IHttpClientFactory Pattern
**Location**: `Services/ApiClient.cs`

The API client uses `IHttpClientFactory` instead of static HttpClient instances to prevent port exhaustion:

```csharp
using var httpClient = _httpClientFactory.CreateClient();
```

Never create HttpClient instances directly. Always use the factory.

### 2. Async Locking with SemaphoreSlim
**Location**: `Services/ConfigService.cs`

Traditional `lock` statements are replaced with `SemaphoreSlim` for async methods:

```csharp
await _semaphore.WaitAsync(cancellationToken);
try { /* critical section */ }
finally { _semaphore.Release(); }
```

**Critical**: Never use `lock` in async methods - it causes deadlocks. Additionally, when holding a `SemaphoreSlim` lock, NEVER call another method that also acquires the same lock - this causes deadlock. If you need to perform operations while holding a lock, implement the logic inline rather than calling other locked methods.

### 3. High-Performance Logging with Channel<T>
**Location**: `Services/LogService.cs`

Logs are batched using `System.Threading.Channels` for 50-100x performance improvement:

```csharp
private readonly Channel<LogEntry> _logChannel;
```

Log entries are queued immediately and flushed in batches (50 entries or 2 seconds). This pattern should be maintained.

### 3b. Batch File Writing with Channel<T>
**Location**: `Services/FileMonitorService.cs`

Translation results are batched before writing to prevent file conflicts with XUnity:

```csharp
private readonly Channel<Dictionary<string, string>> _writeQueue;
private Task? _writerTask;
```

**Key Features**:
- Separate batch writer task runs independently
- Writes every 5 translations OR every 2 seconds (whichever comes first)
- Reduces file I/O by 80%+ and minimizes write conflicts
- Uses retry mechanism (5 attempts, 200-1000ms random delay) to handle remaining conflicts
- **FileSystemWatcher protection**: Temporarily disables file monitoring during batch writes with 300ms stabilization delay to prevent infinite loop

**Critical Channel<T> Patterns**:
- Never write directly to file in consumer tasks. Always queue writes through `_writeQueue.Writer.WriteAsync()`
- Use `WaitToReadAsync()` + `TryRead()` pattern instead of `ReadAsync()` when combining with `Task.WhenAny()` to avoid "Another continuation was already registered" errors
- Store `.AsTask()` result in a variable before using in comparisons - calling it multiple times creates different Task instances
- Initialize Channel BEFORE calling any methods that write to it

**Correct Pattern**:
```csharp
// Store AsTask() result once
var waitToReadTask = _writeQueue.Reader.WaitToReadAsync(cancellationToken).AsTask();
var timeoutTask = Task.Delay(500, cancellationToken);
var completedTask = await Task.WhenAny(waitToReadTask, timeoutTask);

// Use the stored task for comparison
if (completedTask == waitToReadTask && await waitToReadTask)
{
    // Drain available items synchronously
    while (_writeQueue.Reader.TryRead(out var batch))
    {
        pendingWrites.Add(batch);
    }
}
```

**FileSystemWatcher Loop Prevention**:
```csharp
// Disable monitoring during write
bool wasMonitoring = false;
if (_fileWatcher != null && _fileWatcher.EnableRaisingEvents)
{
    _fileWatcher.EnableRaisingEvents = false;
    wasMonitoring = true;
}

try
{
    // Perform file write operations
    await _parser.SaveFileAsync(_monitoredFilePath, cancellationToken);

    // Wait for file system to stabilize
    await Task.Delay(300, cancellationToken);
}
finally
{
    // Always re-enable monitoring
    if (wasMonitoring && _fileWatcher != null)
    {
        _fileWatcher.EnableRaisingEvents = true;
    }
}
```

### 4. Regex Source Generators
**Location**: `Utils/EscapeCharacterHandler.cs`

Uses `[GeneratedRegex]` attribute for 3-140x faster regex matching:

```csharp
[GeneratedRegex(@"\\n", RegexOptions.Compiled)]
private static partial Regex NewlineRegex();
```

Always use source-generated regex instead of `new Regex()` for frequently-used patterns.

### 5. Memory-Efficient String Operations
**Location**: Multiple files (`TextFileParser.cs`, `FileMonitorService.cs`)

Uses `Span<char>` and `ReadOnlySpan<char>` to reduce allocations:

```csharp
ReadOnlySpan<char> lineSpan = line.AsSpan();
string key = lineSpan.Slice(0, separatorIndex).Trim().ToString();
```

Prefer `AsSpan()` over `Substring()` when processing strings in loops.

### 6. CancellationToken Support
**Location**: All async methods in Services and Utils

All async operations support graceful cancellation:

```csharp
public async Task<string> TranslateAsync(..., CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    // ...
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken, timeoutCts.Token);
}
```

Always propagate CancellationToken through async call chains.

### 7. FileShare.ReadWrite for Concurrent File Access
**Location**: `Utils/TextFileParser.cs`

File reading uses `FileShare.ReadWrite` to prevent blocking XUnity's write operations:

```csharp
await using (var fileStream = new FileStream(
    validatedPath,
    FileMode.Open,
    FileAccess.Read,
    FileShare.ReadWrite,  // Critical: allows XUnity to write while we read
    4096,
    FileOptions.Asynchronous))
{
    using var reader = new StreamReader(fileStream, Encoding.UTF8);
    // Read file line by line...
}
```

**Key Points**:
- **Reading**: Use `FileShare.ReadWrite` to allow other processes (XUnity) to read/write simultaneously
- **Writing**: Use `FileShare.Read` (current implementation) to allow reads but prevent concurrent writes
- **Retry mechanism**: Both read and write operations have 5-attempt retry with random delays to handle transient conflicts

**Why this matters**: The default `File.ReadAllLinesAsync()` uses exclusive file locking, which blocks XUnity from adding new text to the translation file. This caused "file in use" errors where XUnity couldn't write while our app was reading.

### 8. Framework-Dependent Deployment
**Location**: `XUnity-LLMTranslatePlus.csproj`, `App.xaml.cs`

The project uses framework-dependent deployment to reduce file size by 53% (from ~84MB to ~40MB):

```xml
<PropertyGroup>
  <WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
  <SelfContained>false</SelfContained>
  <PublishReadyToRun Condition="'$(Configuration)' != 'Debug'">False</PublishReadyToRun>
</PropertyGroup>

<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
</PropertyGroup>
```

**Critical**: Single-file framework-dependent deployment requires setting an environment variable in `App.xaml.cs` constructor:

```csharp
public App()
{
    // Required for PublishSingleFile with framework-dependent deployment
    Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);

    InitializeComponent();
    ConfigureServices();
}
```

**Key Points:**
- **Framework-dependent**: Users must install .NET 9 Desktop Runtime and Windows App SDK 1.8 Runtime before running
- **Size**: Single exe file is ~40MB (vs. ~84MB for self-contained), requires 0MB runtime installation by user
- **ReadyToRun disabled**: R2R pre-compilation increases executable size by 20-40% for marginally faster startup. Disabled to prioritize size.
- **PDB generation disabled**: Debug symbols add 5-20MB and are unnecessary for production builds.
- **IL Trimming unavailable**: `PublishTrimmed` has no effect because WinUI 3/Windows App SDK doesn't support it yet.
- **Single-file deployment**: Requires `EnableMsixTooling=true` even in unpackaged mode for embedded resources.pri generation. Cannot use compression with framework-dependent mode.
- **Build warnings**: MSBuild will warn that "PublishSingleFile is recommended only for Self-Contained apps" - this is just a recommendation, not an error.

## Architecture Patterns

### Custom Exception Hierarchy
**Location**: `Exceptions/TranslationException.cs`

A structured exception system provides detailed error context:

- `TranslationException` - Base class for all translation errors
- `ApiConnectionException` - Network/HTTP errors (includes URL and status code)
- `ApiResponseException` - API response parsing errors (includes response content)
- `TranslationTimeoutException` - Timeout errors (includes timeout duration and retry attempt)
- `ConfigurationValidationException` - Configuration validation errors (includes field name)
- `FileOperationException` - File I/O errors (includes file path and operation type)

When catching exceptions, catch specific types first, then fall back to base types. Never swallow exceptions without logging.

### Security: Path Validation and Encryption
**Location**: `Utils/PathValidator.cs`, `Utils/SecureDataProtection.cs`

All file paths MUST be validated to prevent path traversal attacks:

```csharp
PathValidator.ValidateFileExists(filePath);
string validatedPath = PathValidator.ValidateAndNormalizePath(filePath);
// Use validatedPath, never use filePath directly
```

Sensitive data (API keys) is encrypted using Windows DPAPI:

```csharp
string encrypted = SecureDataProtection.Protect(plainText);
string decrypted = SecureDataProtection.Unprotect(encrypted);
```

API keys in `config.json` are stored encrypted with `"ENC:"` prefix.

### Configuration Validation
**Location**: `Services/ConfigService.cs`

Configuration is validated before saving using the `ValidateConfig()` method:

- API URL format validation
- Numeric range validation (MaxTokens: 1-128000, Temperature: 0-2, MaxConcurrentTranslations: 1-100, etc.)
- Required field validation

Validation errors throw `ConfigurationValidationException` with the specific field name. **Important**: Validation ranges must match UI control limits to prevent save failures.

## XUnity Translation File Format

**Critical**: Understanding the file format is essential for working with `TextFileParser.cs`.

### File Structure
```
// Comments start with //
key1=value1
key2="value with spaces"
```

### Key Characteristics
1. **Literal Escape Sequences**: `\n`, `\r`, `\t` are stored as **literal characters** (backslash + letter), NOT actual newlines. Never use `string.Replace("\\n", "\n")` or similar transformations.

2. **Untranslated Detection**:
   - Basic: `key == value` (exact string match)
   - Advanced: `IsCorruptedTranslation()` detects when value loses escape sequences from key

3. **Quote Handling**: Values may or may not be quoted. Parser preserves original format when saving.

4. **No Backup Files**: v1.0.2+ removed automatic backups. File is directly overwritten.

### Important Methods in TextFileParser.cs
- `ParseFileAsync()` - Reads file, detects untranslated entries
- `SaveFileAsync()` - Writes file while preserving format (comments, empty lines, quotes)
- `UpdateTranslation()` / `UpdateTranslations()` - Updates in-memory translations
- `IsCorruptedTranslation()` - Detects if value lost escape characters from key

## Dependency Injection Setup

**Location**: `App.xaml.cs`

The App constructor performs two critical setup tasks:

1. **Environment Variable for Single-File Deployment** (MUST be first):
```csharp
Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
```
This allows Windows App SDK to locate runtime components when deployed as a single file.

2. **Service Configuration** via `ConfigureServices()`:
```csharp
services.AddHttpClient();  // Required for ApiClient
services.AddSingleton<LogService>();
services.AddSingleton<ConfigService>();
services.AddSingleton<TerminologyService>();
services.AddSingleton<ApiClient>();
services.AddSingleton<TranslationService>();
services.AddSingleton<FileMonitorService>();
services.AddSingleton<TextEditorService>();
```

All services are singletons. Pages resolve services through the DI container:

```csharp
var logService = App.Current.Services.GetService<LogService>();
```

## Translation Pipeline

**Location**: `Services/TranslationService.cs`

Understanding the translation flow is crucial for modifications:

1. **Increment TranslatingCount** - Thread-safe counter increment
   - Tracks active translations (sent to API, awaiting response)
   - Used by UI to display translation queue status

2. **Extract Special Characters** - `EscapeCharacterHandler.ExtractSpecialChars()`
   - Replaces `\n`, `{0}`, `<color>`, etc. with placeholders like `【SPECIAL_0】`
   - Prevents AI from modifying special syntax

3. **Build Context** - `BuildTermsReference()`, `BuildContextReference()`
   - Includes relevant terms from terminology database
   - Adds previous 3 translations for context

4. **Build System Prompt** - Replaces variables in template
   - `{目标语言}` → Target language
   - `{原文}` → Original text
   - `{术语}` → Terms reference
   - `{上下文}` → Context reference

5. **Call API** - `ApiClient.TranslateAsync()`
   - Retries with exponential backoff
   - Supports cancellation token
   - Handles multiple exception types

6. **Apply Terms** - `TerminologyService.ApplyTerms()`
   - Post-processing replacement of terminology

7. **Restore Special Characters** - `EscapeCharacterHandler.SmartRestoreSpecialChars()`
   - Intelligently restores placeholders to original special characters
   - Handles reordering by AI

8. **Update Context Cache** - Stores for next translation

9. **Decrement TranslatingCount** - Always executed in `finally` block
   - Ensures count is accurate even on errors
   - Triggers progress update event

### Translation Statistics
The `TranslationService` tracks four key metrics using **unique text deduplication**:
- `TotalTranslated` - Successfully completed translations (uses `HashSet<string>` for unique count, prevents duplicate counting)
- `TotalFailed` - Failed translation attempts
- `TotalSkipped` - Skipped translations (e.g., already translated)
- `TranslatingCount` - Currently processing (sent to API, awaiting response)

**Important**:
- `TotalTranslated` is backed by `_translatedTexts` HashSet (TranslationService.cs:22) to ensure accurate unique text count even if same text is translated multiple times
- `TranslatingCount` uses thread-safe locking (`_translatingLock`) because multiple threads may be translating concurrently
- Counts are incremented before API call and decremented in `finally` block to ensure accuracy

## File Monitoring System

**Location**: `Services/FileMonitorService.cs`

The monitoring system has specific patterns:

### Cache Management
- `_processedTexts` HashSet tracks translated entries to prevent reprocessing
- **Important**: When an entry becomes untranslated again (e.g., user reset), it's removed from cache
- Check `!entry.IsTranslated` (which uses `IsCorruptedTranslation()`) instead of simple string comparison

### Processing Flow
1. `FileSystemWatcher` detects file changes
2. **Delay 200ms** to let file writing complete (optimized from 500ms, 60% faster detection)
3. `LoadAndProcessFileAsync()` parses file and finds untranslated entries
4. Untranslated entries removed from `_processedTexts` cache (allows retranslation)
5. Added to `_pendingTexts` Channel queue
6. **Consumer tasks** (N concurrent workers) continuously process from queue
7. Each consumer translates text and queues result to `_writeQueue`
8. **Batch writer task** collects translations and writes every 5 items or 2 seconds
9. File write uses retry mechanism with exponential backoff to handle conflicts

### Concurrency Control
```csharp
var semaphore = new SemaphoreSlim(config.MaxConcurrentTranslations, config.MaxConcurrentTranslations);
```

Batch size = `config.MaxConcurrentTranslations * 2` for optimal throughput.

**MaxConcurrentTranslations**: Default is 3 (safe for most APIs), configurable range 1-100 in UI (ApiConfigPage.xaml:209).

### Two-Queue Translation Pipeline

**Critical**: The system uses TWO separate queues by design - they cannot be merged:

1. **`_pendingTexts` Queue** (FileMonitorService:29)
   - Texts detected as untranslated, waiting to be sent to API
   - Displayed as "待翻译" in UI (HomePage)
   - Represents: `Detected but not yet sent to API`

2. **`_translatingCount` Counter** (TranslationService:27)
   - Active API calls currently in flight
   - Displayed as "API处理中" in UI (HomePage)
   - Represents: `Sent to API, awaiting response`

**Pipeline flow:**
```
Untranslated detected → _pendingTexts → API call → _translatingCount → Response → Translated
                        (待翻译)                     (API处理中)
```

This separation is necessary for:
- Throttling API calls independently from detection
- Accurate progress tracking at each stage
- Proper cancellation and error handling per stage

## UI Patterns

### Custom Title Bar and Window Icon
**Location**: `MainWindow.xaml`, `MainWindow.xaml.cs`

The application uses a custom title bar to match the Mica backdrop aesthetic:

```csharp
// In MainWindow constructor - ORDER MATTERS
this.Title = "XUnity大语言模型翻译Plus";  // Window title
AppWindow.SetIcon("ICON.ico");            // Taskbar and Alt+Tab icon

ExtendsContentIntoTitleBar = true;
SetTitleBar(AppTitleBar);
```

```xaml
<!-- Custom title bar grid with app icon -->
<Grid x:Name="AppTitleBar" Grid.Row="0" Height="48">
    <StackPanel Orientation="Horizontal">
        <Image Source="ms-appx:///Assets/Square44x44Logo.targetsize-24_altform-unplated.png"
               Width="16" Height="16" />
        <TextBlock Text="XUnity大语言模型翻译Plus" />
    </StackPanel>
</Grid>
```

**Important**:
- `AppWindow.SetIcon()` sets the taskbar icon, Alt+Tab icon, and window chrome icon
- The title bar must be set BEFORE any navigation occurs
- Title bar element must be non-interactive
- Use `ms-appx:///` URI scheme for embedded assets in title bar

**Window Positioning**: Window is centered on startup using `DisplayArea` API:
```csharp
private void CenterWindow()
{
    var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)?.WorkArea;
    if (area == null) return;

    var x = (area.Value.Width - AppWindow.Size.Width) / 2;
    var y = (area.Value.Height - AppWindow.Size.Height) / 2;
    AppWindow.Move(new PointInt32(x, y));
}
```

### Event-Driven Updates
Services communicate with UI through events:

```csharp
// In service
public event EventHandler<TranslationEntry>? EntryTranslated;
EntryTranslated?.Invoke(this, entry);

// In UI (must use DispatcherQueue for thread safety)
service.EntryTranslated += (sender, entry) =>
{
    DispatcherQueue.TryEnqueue(() =>
    {
        // Update UI controls
    });
};
```

Always use `DispatcherQueue.TryEnqueue()` when updating UI from non-UI threads.

### File Path Display Pattern
**Location**: `HomePage.xaml`, `TextEditorPage.xaml`

Both HomePage and TextEditorPage display the currently monitored/edited file path:

```csharp
// In FileMonitorService - expose current file path
public string? MonitoredFilePath => _monitoredFilePath;

// In HomePage - display when monitoring starts
var filePath = _fileMonitorService.MonitoredFilePath;
if (!string.IsNullOrEmpty(filePath))
{
    FilePathText.Text = filePath;
    FilePathBorder.Visibility = Visibility.Visible;
}
```

```xaml
<!-- Collapsible card showing file path -->
<Border x:Name="FilePathBorder" Visibility="Collapsed">
    <StackPanel>
        <TextBlock Text="监控文件"/>
        <TextBlock x:Name="FilePathText"
                   TextWrapping="Wrap"
                   IsTextSelectionEnabled="True"/>
    </StackPanel>
</Border>
```

Pattern: File path cards are initially collapsed, shown when file is loaded/monitored, and hidden when stopped.

### NavigationView Structure
**Location**: `MainWindow.xaml`

Main navigation uses WinUI NavigationView with Frame:

```csharp
NavView.SelectedItem = NavView.MenuItems[index];
ContentFrame.Navigate(typeof(PageType));
```

Pages are instantiated on navigation. No page caching.

**Available Pages**:
- HomePage - Translation status, start/stop monitoring
- ApiConfigPage - API settings, model selection, connection test
- TranslationSettingsPage - Game directory, manual file path selection, language settings
- TextEditorPage - Manual text editing, CSV import/export
- LogPage - Real-time logs with filtering
- AboutPage - App info, system paths, reset configuration button (in FooterMenuItems)

## Important Conventions

1. **Async All The Way**: All I/O operations are async. Never use `.Result` or `.Wait()`.

2. **Null Safety**: Nullable reference types enabled. Use `?` annotations and null-forgiving operator `!` only when guaranteed non-null.

3. **Resource Paths**: Use `%APPDATA%\XUnity-LLMTranslatePlus\` for all application data.

4. **Error Logging**: Always log exceptions with context using LogService before throwing/returning.

5. **Thread Safety**: Use `lock` for simple sync operations, `SemaphoreSlim` for async operations.

6. **Dictionary Lookups**: Use `TryGetValue()` instead of `ContainsKey()` + indexer for performance.

## Common Gotchas

1. **WinUI 3 Threading**: UI updates MUST be on UI thread. Always use `DispatcherQueue.TryEnqueue()`.

2. **XUnity File Format**: Never escape/unescape `\n` as actual newlines. Store literally as two characters.

3. **FileSystemWatcher Delays**: Current delay is 200ms (optimized from 500ms). This is the minimum safe delay for most file systems.

4. **Config Load Timing**: Config must be loaded before accessing `GetCurrentConfig()`. App.xaml.cs handles this during startup.

5. **Service Disposal**: FileMonitorService and LogService implement IDisposable. Ensure proper cleanup on app shutdown.

6. **XAML Build Errors**: If XAML fails to compile, check for CS errors first - they cascade to XAML compiler.

7. **TranslatingCount Threading**: When reading `TranslatingCount`, the property handles locking internally. When incrementing/decrementing, always use the `_translatingLock` object to ensure thread safety.

8. **Custom Title Bar**: The title bar element (`AppTitleBar`) must be non-interactive. Interactive elements in the title bar area will not receive input events.

9. **Framework-Dependent Deployment Size**: Release builds produce a ~40MB single exe file. Users must install .NET 9 Desktop Runtime and Windows App SDK 1.8 Runtime. See `RUNTIME-INSTALL.md` for user installation instructions.

10. **Single-File Deployment Requirements**:
    - WinUI 3 single-file publish requires `EnableMsixTooling=true` even in unpackaged mode, otherwise build fails with error about resources.pri generation.
    - Framework-dependent single-file deployment cannot use compression (`EnableCompressionInSingleFile` is not supported).
    - MUST set environment variable `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY` in `App.xaml.cs` constructor before `InitializeComponent()`.

11. **Build Warnings**: When building framework-dependent single-file, MSBuild will show warnings about "PublishSingleFile is recommended only for Self-Contained apps". These are recommendations, not errors. The build will succeed and the executable will work correctly if the environment variable is set.

12. **ConfigService Deadlock**: The `LoadConfigAsync` method in ConfigService holds a `_semaphore` lock. It MUST NOT call `SaveConfigAsync` (which also acquires the same lock), as this causes deadlock. If you need to save config during load, serialize and write the file inline within the locked section. This pattern applies to any class using SemaphoreSlim for synchronization.

13. **File Write Conflicts**: XUnity and the app may write to the same file simultaneously. Always use the retry mechanism in `TextFileParser.SaveFileAsync()` which attempts up to 5 times with random 200-1000ms delays. The batch writing system in FileMonitorService further reduces conflict probability by 80%+.

14. **Manual Translation File Path**: Users can manually specify a translation file path (TranslationSettingsPage) which takes priority over auto-detection. FileMonitorService checks `config.ManualTranslationFilePath` first before attempting auto-detection.

15. **TextEditor Empty State**: Always check if game directory/file path is configured before loading in TextEditorPage. Use 10-second timeout protection and show empty state UI with helpful message if path is missing.

16. **Channel<T> with Task.WhenAny**: NEVER use `ReadAsync()` directly with `Task.WhenAny()` - this causes "Another continuation was already registered" errors when timeout occurs. Always use `WaitToReadAsync()` + `TryRead()` pattern instead. Additionally, ALWAYS store `.AsTask()` result in a variable before using it in comparisons - calling it multiple times creates different Task instances, breaking the comparison logic.

17. **Channel Initialization Order**: Channels MUST be initialized BEFORE any code that writes to them runs. In FileMonitorService, `_pendingTexts` Channel must be created before calling `LoadAndProcessFileAsync()`, otherwise all initial texts will silently fail to queue.

18. **FileSystemWatcher Infinite Loop**: When your code writes to a file being monitored by FileSystemWatcher, it triggers its own `Changed` event, creating an infinite loop. Always temporarily disable `FileSystemWatcher.EnableRaisingEvents` during file writes, add a stabilization delay (300ms), and re-enable in a `finally` block. This is critical for batch write operations in FileMonitorService.

19. **File Read Locking**: NEVER use `File.ReadAllLinesAsync()` or similar methods without specifying `FileShare` parameters when reading files that other processes (like XUnity) may write to. The default behavior uses exclusive locking, preventing other processes from writing. Always use `FileStream` with `FileShare.ReadWrite` for reading shared files. Similarly, use `FileShare.Read` when writing to allow concurrent reads. TextFileParser.cs implements this pattern correctly with retry mechanisms for both operations.

## Testing Translation API

Use the built-in connection test:

```csharp
bool success = await apiClient.TestConnectionAsync(config, cancellationToken);
```

This sends "Hello" with prompt "Translate to Chinese:" to verify connectivity.

## Performance Considerations

- **Batch Operations**: Always prefer batch translation over individual calls when processing multiple entries
- **Concurrent Limits**: Default `MaxConcurrentTranslations` is 3, configurable up to 100. Higher values may cause rate limiting or API errors depending on the provider.
- **Log Verbosity**: Use `LogLevel.Debug` sparingly - it generates significant I/O
- **Memory**: TextFileParser keeps file contents in memory. Large translation files (>100K entries) may need optimization.
- **UI Performance**:
  - LogPage displays only the most recent 500 log entries to prevent UI lag
  - Log export retrieves full log history from LogService
  - HomePage limits recent translations to 8 entries
  - Use UI virtualization (ItemsStackPanel) for large lists

## Version History Context

- **v1.0.1**: Added CSV/TXT import/export to TextEditorPage
- **v1.0.2**: Removed backup files, optimized ApiConfigPage UI, fixed LogPage filter state persistence
- **v1.0.3**: Fixed corrupted translation detection, improved cache management in FileMonitorService
- **Recent Optimizations**:
  - Added comprehensive optimizations (IHttpClientFactory, Channel<T>, Regex source generators, Span<char>, CancellationToken, custom exceptions, path security, DPAPI encryption)
  - Implemented custom title bar with Mica backdrop integration
  - Added real-time translation queue tracking (`TranslatingCount`)
  - UI improvements: Updated status displays across HomePage and LogPage
  - Removed unused features: AI translation button in TextEditor, context weight slider (field reserved for future use)
  - Enhanced log export to export all logs (not just displayed subset)
- **Build Optimizations**:
  - Created `Build-Release.ps1` one-click build script for automated releases
  - Disabled ReadyToRun compilation for Release builds (20-40% size reduction, ~100-200ms slower startup)
  - Disabled PDB generation for Release builds (5-20MB savings)
  - Removed ineffective PublishTrimmed setting (WinUI 3 doesn't support IL trimming)
  - Added `EnableMsixTooling=true` for single-file deployment support
- **Deployment Mode Change**:
  - Switched from self-contained to framework-dependent deployment (53% size reduction)
  - Single exe size reduced from ~84MB to ~40MB
  - Users must now install .NET 9 Desktop Runtime and Windows App SDK 1.8 Runtime
  - Added environment variable setup in `App.xaml.cs` for single-file support
  - Created `RUNTIME-INSTALL.md` with user installation instructions
  - Single-file deployment without compression (compression not supported for framework-dependent)
- **Recent UI Improvements**:
  - Fixed window and taskbar icon display with `AppWindow.SetIcon("ICON.ico")`
  - Fixed title bar icon using `ms-appx:///` URI scheme for embedded assets
  - Added automatic window centering on startup using `DisplayArea` API
  - Increased MaxConcurrentTranslations limit from 20 to 100
  - Added file path display to HomePage and TextEditorPage for better user feedback
  - Renamed "翻译队列" to "API处理中" for clarity in two-queue pipeline
  - Removed non-functional "全部翻译" button from TextEditorPage
  - Added `MonitoredFilePath` property to FileMonitorService for UI access
- **Critical Bugfixes and Performance Improvements** (Latest):
  - Fixed deadlock in ConfigService.LoadConfigAsync that prevented config file generation on first startup
  - Fixed validation range mismatch: ConfigService MaxConcurrentTranslations validation now matches UI limit (1-100)
  - LoadConfigAsync now serializes config inline when creating default config instead of calling SaveConfigAsync (prevents reentrancy deadlock)
  - **Fixed duplicate translation counting**: TotalTranslated now uses HashSet for unique text tracking (30 texts = 30 count, not 200+)
  - **Fixed file write conflicts**: Implemented retry mechanism (5 attempts) and batch writing system (5 items/2s) to handle XUnity concurrent writes
  - **Fixed TextEditor hang**: Added empty path detection, 10s timeout, and friendly empty state UI
  - **Optimized translation latency**: Reduced file change detection from 500ms to 200ms (60% faster), removed redundant file parsing in translation loop
  - **Added manual file path**: Users can now manually select translation file, bypassing auto-detection issues
  - **Added AboutPage**: New page with app info, system paths, and configuration reset functionality
  - **Fixed Channel<T> continuation conflict**: Changed from `ReadAsync()` to `WaitToReadAsync()` + `TryRead()` pattern in BatchWriterTaskAsync to prevent "Another continuation was already registered" errors
  - **Fixed Task.WhenAny comparison**: Stored `.AsTask()` result in variable before comparison instead of calling it multiple times (each call creates new Task instance)
  - **Fixed Channel initialization order**: Moved Channel initialization before `LoadAndProcessFileAsync()` to prevent silent queue write failures on startup
  - **Fixed FileSystemWatcher infinite loop**: Implemented temporary disabling of file monitoring during batch writes with 300ms stabilization delay to prevent duplicate translations caused by self-triggered file change events
  - **Removed Console.WriteLine**: Migrated all console output to LogService (except LogService self-error handling)
  - **Removed MonitorInterval setting**: Deleted unused monitoring interval configuration from UI and config
  - **Fixed retry cancellation**: ApiClient retry delays now properly support CancellationToken for high-concurrency scenarios
  - **Updated MaxTokens default**: Changed from 2000 to 4096 to better accommodate modern LLM context windows
  - **Improved manual file path UX**: Manual file path option now only appears when auto-detection is disabled or fails
  - **CRITICAL: Fixed file locking issue**: Changed `ParseFileAsync()` to use `FileStream` with `FileShare.ReadWrite` instead of `File.ReadAllLinesAsync()`, preventing app from blocking XUnity's write operations. Added retry mechanism (5 attempts, 100-500ms random delay) for read operations. This resolves the persistent "file in use" error where XUnity couldn't add new text while our app was reading the translation file.
