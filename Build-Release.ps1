# Build script for XUnity-LLMTranslatePlus
# Generates framework-dependent single-file release build

param(
    [string]$Runtime = "win-x64",
    [switch]$SkipClean
)

# Script configuration
$ErrorActionPreference = "Stop"
$ProjectPath = "XUnity-LLMTranslatePlus\XUnity-LLMTranslatePlus.csproj"
$OutputDir = "Release"
$Configuration = "Release"

# Helper function for error handling
function Exit-WithError {
    param([string]$Message)
    Write-Host ""
    Write-Host "ERROR: $Message" -ForegroundColor Red
    Write-Host ""
    Write-Host "Press any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# Display banner
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "XUnity-LLMTranslatePlus Release Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Verify project file exists
if (-not (Test-Path $ProjectPath)) {
    Exit-WithError "Project file not found at: $ProjectPath"
}

Write-Host "[1/4] Verifying .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "      Version: $dotnetVersion" -ForegroundColor Green
} catch {
    Exit-WithError ".NET SDK not found. Please install .NET 9.0 SDK or later."
}

# Clean previous builds
if (-not $SkipClean) {
    Write-Host "[2/4] Cleaning previous builds..." -ForegroundColor Yellow
    try {
        dotnet clean $ProjectPath --configuration $Configuration --verbosity quiet | Out-Null
        if (Test-Path $OutputDir) {
            Remove-Item -Path $OutputDir -Recurse -Force
        }
        Write-Host "      Done" -ForegroundColor Green
    } catch {
        Write-Host "      WARNING: Clean operation failed (continuing)" -ForegroundColor DarkYellow
    }
} else {
    Write-Host "[2/4] Skipping clean" -ForegroundColor Yellow
}

# Build the project
Write-Host "[3/4] Building release..." -ForegroundColor Yellow
try {
    dotnet publish $ProjectPath `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained false `
        --output $OutputDir `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableMsixTooling=true `
        --verbosity quiet

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Write-Host "      Done" -ForegroundColor Green
} catch {
    Exit-WithError "Build failed: $($_.Exception.Message)"
}

# Verify output
Write-Host "[4/4] Verifying output..." -ForegroundColor Yellow
$ExePath = Join-Path $OutputDir "XUnity-LLMTranslatePlus.exe"
if (Test-Path $ExePath) {
    $FileSize = (Get-Item $ExePath).Length
    $FileSizeMB = [math]::Round($FileSize / 1MB, 2)
    Write-Host "      Size: $FileSizeMB MB" -ForegroundColor Green
} else {
    Exit-WithError "Executable not found at: $ExePath"
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Output: $(Resolve-Path $OutputDir)\XUnity-LLMTranslatePlus.exe" -ForegroundColor White
Write-Host ""
Write-Host "Runtime requirements:" -ForegroundColor Yellow
Write-Host "  - .NET 9 Desktop Runtime (x64)" -ForegroundColor Gray
Write-Host "  - Windows App SDK 1.8 Runtime" -ForegroundColor Gray
Write-Host ""
Write-Host "Press any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
