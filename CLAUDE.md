# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

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
1. Scan → Find .assets, .bundle, .unity3d files
2. Extract → TextAsset, MonoBehaviour fields, GameObject names
3. Filter → CJK detection, regex exclusions
4. Translate batch → Use existing TranslationService
5. Merge → Combine with XUnity translation file

**Backend Support**:
- **Mono/.NET**: MonoCecilTempGenerator with Managed/*.dll
- **IL2CPP**: Cpp2IlTempGenerator with global-metadata.dat + GameAssembly
- **Auto-detection**: Mono → IL2CPP fallback

**Configuration**:
- `ScanTextAssets` / `ScanMonoBehaviours`
- `MonoBehaviourFields`: Configurable field names (text, dialogText, description, etc.)
- `SourceLanguageFilter`: All/CJK/Chinese/Japanese/English/Korean/Russian
- `ExcludePatterns`: Regex to filter paths/variables
- `ClassDatabasePath`: Optional custom classdata.tpk (embedded by default)

**Critical Details**:
- `IsDummy` is property: `field.IsDummy` (not method)
- ClassDatabase: Uses embedded `Resources/classdata.tpk` → User path fallback
- TextAsset `m_Script` is **byte[]**: `AsByteArray` then `Encoding.UTF8.GetString()`
- Use `fileInst.path` NOT string parameter for Managed folder path
- Bundle extraction: `LoadBundleFile(path, unpackIfPacked: true)` for LZMA/LZ4

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
