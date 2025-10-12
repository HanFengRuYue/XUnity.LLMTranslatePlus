# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**XUnity大语言模型翻译Plus** - A WinUI 3 application that integrates with XUnity.AutoTranslator to provide AI-powered game text translation using Large Language Models.

- **Framework**: .NET 9.0 + WinUI 3
- **Runtime**: Unpackaged (WindowsPackageType=None, WindowsAppSDKSelfContained=true)
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
# Self-contained publish for distribution
dotnet publish --configuration Release --runtime win-x64 --self-contained true

# Trimmed publish (smaller size, enabled in Release by default)
dotnet publish --configuration Release --runtime win-x64 /p:PublishTrimmed=true
```

**Note**: This project uses WinUI 3 in Unpackaged mode, so MSIX packaging is disabled. The executable runs directly without requiring installation.

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

Never use `lock` in async methods - it causes deadlocks.

### 3. High-Performance Logging with Channel<T>
**Location**: `Services/LogService.cs`

Logs are batched using `System.Threading.Channels` for 50-100x performance improvement:

```csharp
private readonly Channel<LogEntry> _logChannel;
```

Log entries are queued immediately and flushed in batches (50 entries or 2 seconds). This pattern should be maintained.

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
- Numeric range validation (MaxTokens: 1-128000, Temperature: 0-2, etc.)
- Required field validation

Validation errors throw `ConfigurationValidationException` with the specific field name.

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

Services are configured in the `ConfigureServices()` method:

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

1. **Extract Special Characters** - `EscapeCharacterHandler.ExtractSpecialChars()`
   - Replaces `\n`, `{0}`, `<color>`, etc. with placeholders like `【SPECIAL_0】`
   - Prevents AI from modifying special syntax

2. **Build Context** - `BuildTermsReference()`, `BuildContextReference()`
   - Includes relevant terms from terminology database
   - Adds previous 3 translations for context

3. **Build System Prompt** - Replaces variables in template
   - `{目标语言}` → Target language
   - `{原文}` → Original text
   - `{术语}` → Terms reference
   - `{上下文}` → Context reference

4. **Call API** - `ApiClient.TranslateAsync()`
   - Retries with exponential backoff
   - Supports cancellation token
   - Handles multiple exception types

5. **Apply Terms** - `TerminologyService.ApplyTerms()`
   - Post-processing replacement of terminology

6. **Restore Special Characters** - `EscapeCharacterHandler.SmartRestoreSpecialChars()`
   - Intelligently restores placeholders to original special characters
   - Handles reordering by AI

7. **Update Context Cache** - Stores for next translation

## File Monitoring System

**Location**: `Services/FileMonitorService.cs`

The monitoring system has specific patterns:

### Cache Management
- `_processedTexts` HashSet tracks translated entries to prevent reprocessing
- **Important**: When an entry becomes untranslated again (e.g., user reset), it's removed from cache
- Check `!entry.IsTranslated` (which uses `IsCorruptedTranslation()`) instead of simple string comparison

### Processing Flow
1. `FileSystemWatcher` detects file changes
2. Delay 500ms to let file writing complete
3. `LoadAndProcessFileAsync()` parses file and finds untranslated entries
4. Untranslated entries removed from `_processedTexts` cache (allows retranslation)
5. Added to `_pendingTexts` queue
6. `ProcessPendingTextsAsync()` runs every 5 seconds
7. Validates entries still need translation (file may have changed)
8. Calls `TranslateBatchAsync()` with controlled concurrency
9. Updates file atomically

### Concurrency Control
```csharp
var semaphore = new SemaphoreSlim(config.MaxConcurrentTranslations, config.MaxConcurrentTranslations);
```

Batch size = `config.MaxConcurrentTranslations * 2` for optimal throughput.

## UI Patterns

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

### NavigationView Structure
**Location**: `MainWindow.xaml`

Main navigation uses WinUI NavigationView with Frame:

```csharp
NavView.SelectedItem = NavView.MenuItems[index];
ContentFrame.Navigate(typeof(PageType));
```

Pages are instantiated on navigation. No page caching.

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

3. **FileSystemWatcher Delays**: Always add 500ms delay before reading file after change event.

4. **Config Load Timing**: Config must be loaded before accessing `GetCurrentConfig()`. App.xaml.cs handles this during startup.

5. **Service Disposal**: FileMonitorService and LogService implement IDisposable. Ensure proper cleanup on app shutdown.

6. **XAML Build Errors**: If XAML fails to compile, check for CS errors first - they cascade to XAML compiler.

## Testing Translation API

Use the built-in connection test:

```csharp
bool success = await apiClient.TestConnectionAsync(config, cancellationToken);
```

This sends "Hello" with prompt "Translate to Chinese:" to verify connectivity.

## Performance Considerations

- **Batch Operations**: Always prefer batch translation over individual calls when processing multiple entries
- **Concurrent Limits**: Default `MaxConcurrentTranslations` is 3. Higher values may cause rate limiting.
- **Log Verbosity**: Use `LogLevel.Debug` sparingly - it generates significant I/O
- **Memory**: TextFileParser keeps file contents in memory. Large translation files (>100K entries) may need optimization.

## Version History Context

- **v1.0.1**: Added CSV/TXT import/export to TextEditorPage
- **v1.0.2**: Removed backup files, optimized ApiConfigPage UI, fixed LogPage filter state persistence
- **v1.0.3**: Fixed corrupted translation detection, improved cache management in FileMonitorService
- **Current**: Added comprehensive optimizations (IHttpClientFactory, Channel<T>, Regex source generators, Span<char>, CancellationToken, custom exceptions, path security, DPAPI encryption)
