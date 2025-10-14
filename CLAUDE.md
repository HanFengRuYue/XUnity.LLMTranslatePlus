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

# Release (Recommended)
.\Build-Release.ps1

# Manual publish
dotnet publish --configuration Release --runtime win-x64 --self-contained false /p:PublishSingleFile=true
```

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
**FileMonitorService.cs**: Batches file writes (5 translations or 2s) to prevent XUnity conflicts

**Critical Patterns**:
- Initialize Channel BEFORE any code writes to it
- Use `WaitToReadAsync()` + `TryRead()` with `Task.WhenAny()` (not `ReadAsync()`)
- Store `.AsTask()` result in variable - calling multiple times creates different Task instances
- Disable FileSystemWatcher during writes (300ms stabilization delay) to prevent infinite loops

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

1. Increment `TranslatingCount` (thread-safe counter)
2. Extract special characters → placeholders (e.g., `【SPECIAL_0】`)
3. Build context (N recent translations, thread-safe with `_contextLock`)
4. Build system prompt (replaces `{目标语言}`, `{原文}`, `{术语}`, `{上下文}`)
5. Call API with retry + exponential backoff
6. Apply terminology post-processing
7. Restore special characters intelligently
8. Update context cache (thread-safe: `AddToContext(original, translated, config)`)
9. Smart terminology extraction (async background with `Task.Run`, non-blocking)
10. Decrement `TranslatingCount` (in `finally` block)

**Statistics**: `TotalTranslated` uses `HashSet<string>` for unique count deduplication.

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

Pages resolve via: `App.Current.Services.GetService<LogService>()`

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

**Critical**: `GetConfigFromUIAsync()` must ALWAYS load fresh config first via `LoadConfigAsync()` to preserve settings from other pages, then only update relevant fields.

### Pages
- HomePage - Status, start/stop monitoring
- ApiConfigPage - API config, connection test
- TranslationSettingsPage - Game directory, language, context, system prompt
- TerminologyPage - Terminology CRUD, file management, smart extraction toggle
- TextEditorPage - Manual editing, CSV import/export
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

## Performance Notes

- **Concurrent Limits**: Default 3, max 100. Higher values may hit rate limits.
- **Log Verbosity**: `LogLevel.Debug` generates significant I/O.
- **Memory**: TextFileParser keeps file in memory. Large files (>100K entries) may need optimization.
- **UI**: LogPage shows 500 recent entries. HomePage shows 8 recent translations.

## Exception Hierarchy (Exceptions/TranslationException.cs)

- `TranslationException` - Base
- `ApiConnectionException` - Network/HTTP
- `ApiResponseException` - API parsing
- `TranslationTimeoutException` - Timeout
- `ConfigurationValidationException` - Config validation
- `FileOperationException` - File I/O

Catch specific types first, then base types. Never swallow exceptions without logging.
