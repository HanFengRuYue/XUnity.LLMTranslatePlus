# Build script for XUnity-LLMTranslatePlus
# Generates a self-contained release build in the Release folder

param(
    [string]$Runtime = "win-x64",
    [switch]$SkipClean
)

# Script configuration
$ErrorActionPreference = "Stop"
$ProjectPath = "XUnity-LLMTranslatePlus\XUnity-LLMTranslatePlus.csproj"
$OutputDir = "Release"
$Configuration = "Release"

# Display banner
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "XUnity-LLMTranslatePlus Release Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Verify project file exists
if (-not (Test-Path $ProjectPath)) {
    Write-Host "ERROR: Project file not found at: $ProjectPath" -ForegroundColor Red
    exit 1
}

Write-Host "[1/4] Verifying environment..." -ForegroundColor Yellow
# Check if dotnet is available
try {
    $dotnetVersion = dotnet --version
    Write-Host "      .NET SDK version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "ERROR: .NET SDK not found. Please install .NET 9.0 SDK or later." -ForegroundColor Red
    exit 1
}

# Clean previous builds
if (-not $SkipClean) {
    Write-Host "[2/4] Cleaning previous builds..." -ForegroundColor Yellow
    try {
        dotnet clean $ProjectPath --configuration $Configuration --verbosity quiet
        if (Test-Path $OutputDir) {
            Remove-Item -Path $OutputDir -Recurse -Force
            Write-Host "      Removed existing Release folder" -ForegroundColor Green
        }
    } catch {
        Write-Host "WARNING: Clean operation encountered issues (continuing anyway)" -ForegroundColor DarkYellow
    }
} else {
    Write-Host "[2/4] Skipping clean (--SkipClean specified)" -ForegroundColor Yellow
}

# Build the project
Write-Host "[3/4] Publishing release build..." -ForegroundColor Yellow
Write-Host "      Configuration: $Configuration" -ForegroundColor Gray
Write-Host "      Runtime: $Runtime" -ForegroundColor Gray
Write-Host "      Self-contained: No (Framework-Dependent)" -ForegroundColor Cyan
Write-Host "      Single file: Yes (without compression)" -ForegroundColor Green
Write-Host "      ReadyToRun: No (size optimization)" -ForegroundColor Gray
Write-Host "      PDB files: No (disabled)" -ForegroundColor Gray
Write-Host ""

try {
    dotnet publish $ProjectPath `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained false `
        --output $OutputDir `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableMsixTooling=true

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
} catch {
    Write-Host ""
    Write-Host "ERROR: Build failed!" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

# Verify output
Write-Host ""
Write-Host "[4/4] Verifying output..." -ForegroundColor Yellow
$ExePath = Join-Path $OutputDir "XUnity-LLMTranslatePlus.exe"
if (Test-Path $ExePath) {
    $FileSize = (Get-Item $ExePath).Length
    $FileSizeMB = [math]::Round($FileSize / 1MB, 2)
    Write-Host "      Executable: $ExePath" -ForegroundColor Green
    Write-Host "      Size: $FileSizeMB MB" -ForegroundColor Green
} else {
    Write-Host "ERROR: Expected executable not found at: $ExePath" -ForegroundColor Red
    exit 1
}

# List additional files (WinUI may require some companion files)
$AllFiles = Get-ChildItem -Path $OutputDir -File | Where-Object { $_.Extension -in @('.exe', '.dll', '.json', '.winmd', '.pri') }
$FileCount = $AllFiles.Count

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Output directory: $(Resolve-Path $OutputDir)" -ForegroundColor White
Write-Host "Total files: $FileCount" -ForegroundColor White
Write-Host ""
Write-Host "DEPLOYMENT MODE: Framework-Dependent" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "SIZE OPTIMIZATION:" -ForegroundColor Green
Write-Host "  - Deployment size: ~20-30MB (70-80% reduction)" -ForegroundColor Gray
Write-Host "  - PDB files: Disabled" -ForegroundColor Gray
Write-Host "  - ReadyToRun: Disabled" -ForegroundColor Gray
Write-Host ""
Write-Host "RUNTIME REQUIREMENTS:" -ForegroundColor Yellow
Write-Host "  Users must install the following runtimes:" -ForegroundColor Gray
Write-Host "  1. .NET 9 Desktop Runtime (x64)" -ForegroundColor White
Write-Host "     Download: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Gray
Write-Host "  2. Windows App SDK 1.8 Runtime" -ForegroundColor White
Write-Host "     (Automatically installed from Microsoft Store or can be manually deployed)" -ForegroundColor Gray
Write-Host ""
Write-Host "See RUNTIME-INSTALL.md for detailed installation instructions." -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
