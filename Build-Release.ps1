# Build script for XUnity-LLMTranslatePlus
# Generates framework-dependent single-file release builds for multiple architectures

param(
    [switch]$SkipClean
)

# Script configuration
$ErrorActionPreference = "Stop"
$ProjectPath = "XUnity-LLMTranslatePlus\XUnity-LLMTranslatePlus.csproj"
$OutputBaseDir = "Release"
$Configuration = "Release"
$Runtimes = @("win-x86", "win-x64", "win-arm64")
$SevenZipPath = "C:\Program Files\7-Zip\7z.exe"

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

Write-Host "[1/5] Verifying .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "      Version: $dotnetVersion" -ForegroundColor Green
} catch {
    Exit-WithError ".NET SDK not found. Please install .NET 9.0 SDK or later."
}

Write-Host "[2/5] Verifying 7-Zip..." -ForegroundColor Yellow
if (-not (Test-Path $SevenZipPath)) {
    Exit-WithError "7-Zip not found at: $SevenZipPath`nPlease install 7-Zip or update the path in the script."
}
Write-Host "      Found: $SevenZipPath" -ForegroundColor Green

# Clean previous builds
if (-not $SkipClean) {
    Write-Host "[3/5] Cleaning previous builds..." -ForegroundColor Yellow
    try {
        dotnet clean $ProjectPath --configuration $Configuration --verbosity quiet | Out-Null
        if (Test-Path $OutputBaseDir) {
            Remove-Item -Path $OutputBaseDir -Recurse -Force
        }
        Write-Host "      Done" -ForegroundColor Green
    } catch {
        Write-Host "      WARNING: Clean operation failed (continuing)" -ForegroundColor DarkYellow
    }
} else {
    Write-Host "[3/5] Skipping clean" -ForegroundColor Yellow
}

# Build projects for all runtimes
Write-Host "[4/5] Building releases for all architectures..." -ForegroundColor Yellow
Write-Host ""

$BuildResults = @()
$CurrentBuild = 0

foreach ($Runtime in $Runtimes) {
    $CurrentBuild++
    Write-Host "  [$CurrentBuild/$($Runtimes.Count)] Building $Runtime..." -ForegroundColor Cyan

    $OutputDir = Join-Path $OutputBaseDir $Runtime
    $ExeName = "XUnity-LLMTranslatePlus.exe"
    $ExePath = Join-Path $OutputDir $ExeName
    $ZipName = "XUnity-LLMTranslatePlus-$Runtime.zip"
    $ZipPath = [System.IO.Path]::GetFullPath((Join-Path $OutputBaseDir $ZipName))

    try {
        # Build
        Write-Host "      - Compiling..." -ForegroundColor Gray
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

        # Verify exe exists
        if (-not (Test-Path $ExePath)) {
            throw "Executable not found at: $ExePath"
        }

        $ExeSize = (Get-Item $ExePath).Length
        $ExeSizeMB = [math]::Round($ExeSize / 1MB, 2)
        Write-Host "      - EXE size: $ExeSizeMB MB" -ForegroundColor Gray

        # Compress with 7-Zip (only pack the exe file without folder structure)
        Write-Host "      - Compressing with 7-Zip (maximum)..." -ForegroundColor Gray
        Push-Location $OutputDir
        try {
            & $SevenZipPath a -tzip -mx=9 $ZipPath $ExeName | Out-Null

            if ($LASTEXITCODE -ne 0) {
                throw "7-Zip compression failed with exit code $LASTEXITCODE"
            }
        } finally {
            Pop-Location
        }

        # Verify zip exists
        if (-not (Test-Path $ZipPath)) {
            throw "Zip file not found at: $ZipPath"
        }

        $ZipSize = (Get-Item $ZipPath).Length
        $ZipSizeMB = [math]::Round($ZipSize / 1MB, 2)
        $CompressionRatio = [math]::Round(($ZipSize / $ExeSize) * 100, 1)
        Write-Host "      - ZIP size: $ZipSizeMB MB ($CompressionRatio% of original)" -ForegroundColor Gray

        Write-Host "      ✓ Completed" -ForegroundColor Green

        # Store build result
        $BuildResults += [PSCustomObject]@{
            Runtime = $Runtime
            ExePath = $ExePath
            ExeSizeMB = $ExeSizeMB
            ZipPath = $ZipPath
            ZipSizeMB = $ZipSizeMB
            CompressionRatio = $CompressionRatio
            Success = $true
        }

    } catch {
        Write-Host "      ✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
        $BuildResults += [PSCustomObject]@{
            Runtime = $Runtime
            Success = $false
            Error = $_.Exception.Message
        }
    }

    Write-Host ""
}

# Verify at least one build succeeded
$SuccessfulBuilds = $BuildResults | Where-Object { $_.Success -eq $true }
if ($SuccessfulBuilds.Count -eq 0) {
    Exit-WithError "All builds failed. Please check the errors above."
}

# Display build summary
Write-Host "[5/5] Build Summary" -ForegroundColor Yellow
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Build Completed!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

Write-Host "Successful Builds: $($SuccessfulBuilds.Count)/$($Runtimes.Count)" -ForegroundColor White
Write-Host ""

foreach ($result in $BuildResults) {
    if ($result.Success) {
        Write-Host "✓ $($result.Runtime)" -ForegroundColor Green
        Write-Host "  EXE: $($result.ExeSizeMB) MB" -ForegroundColor Gray
        Write-Host "  ZIP: $($result.ZipSizeMB) MB ($($result.CompressionRatio)%)" -ForegroundColor Gray
        Write-Host "  Path: $(Resolve-Path $result.ZipPath)" -ForegroundColor White
        Write-Host ""
    } else {
        Write-Host "✗ $($result.Runtime) - Failed" -ForegroundColor Red
        Write-Host "  Error: $($result.Error)" -ForegroundColor DarkRed
        Write-Host ""
    }
}

Write-Host "Output directory: $(Resolve-Path $OutputBaseDir)" -ForegroundColor Cyan
Write-Host ""
Write-Host "Runtime requirements:" -ForegroundColor Yellow
Write-Host "  - .NET 9 Desktop Runtime (architecture-specific)" -ForegroundColor Gray
Write-Host "  - Windows App SDK 1.8 Runtime" -ForegroundColor Gray
Write-Host ""
Write-Host "Press any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
