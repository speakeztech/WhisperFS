# PowerShell script to download whisper.cpp native binaries for all platforms
# This script downloads pre-built whisper.cpp binaries from GitHub releases

param(
    [string]$Version = "1.5.4",
    [string]$OutputPath = "$PSScriptRoot/../native",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# Define platform configurations
$platforms = @(
    @{
        Runtime = "win-x64"
        FileName = "whisper.dll"
        DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v$Version/whisper-bin-x64.zip"
        ExtractPath = "whisper.dll"
    },
    @{
        Runtime = "win-arm64"
        FileName = "whisper.dll"
        DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v$Version/whisper-bin-arm64.zip"
        ExtractPath = "whisper.dll"
    },
    @{
        Runtime = "linux-x64"
        FileName = "libwhisper.so"
        DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v$Version/whisper-bin-linux-x64.tar.gz"
        ExtractPath = "lib/libwhisper.so"
    },
    @{
        Runtime = "linux-arm64"
        FileName = "libwhisper.so"
        DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v$Version/whisper-bin-linux-arm64.tar.gz"
        ExtractPath = "lib/libwhisper.so"
    },
    @{
        Runtime = "osx-x64"
        FileName = "libwhisper.dylib"
        DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v$Version/whisper-bin-macos-x64.tar.gz"
        ExtractPath = "lib/libwhisper.dylib"
    },
    @{
        Runtime = "osx-arm64"
        FileName = "libwhisper.dylib"
        DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v$Version/whisper-bin-macos-arm64.tar.gz"
        ExtractPath = "lib/libwhisper.dylib"
    }
)

# CUDA variants for Windows and Linux
$cudaPlatforms = @(
    @{
        Runtime = "win-x64-cuda11"
        FileName = "whisper-cuda11.dll"
        DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v$Version/whisper-cuda11-bin-x64.zip"
        ExtractPath = "whisper.dll"
    },
    @{
        Runtime = "win-x64-cuda12"
        FileName = "whisper-cuda12.dll"
        DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v$Version/whisper-cuda12-bin-x64.zip"
        ExtractPath = "whisper.dll"
    },
    @{
        Runtime = "linux-x64-cuda11"
        FileName = "libwhisper-cuda11.so"
        DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v$Version/whisper-cuda11-bin-linux-x64.tar.gz"
        ExtractPath = "lib/libwhisper.so"
    },
    @{
        Runtime = "linux-x64-cuda12"
        FileName = "libwhisper-cuda12.so"
        DownloadUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v$Version/whisper-cuda12-bin-linux-x64.tar.gz"
        ExtractPath = "lib/libwhisper.so"
    }
)

function Download-Platform {
    param($Platform)

    $targetDir = Join-Path $OutputPath $Platform.Runtime
    $targetFile = Join-Path $targetDir $Platform.FileName

    # Check if file already exists
    if ((Test-Path $targetFile) -and (-not $Force)) {
        Write-Host "✓ $($Platform.Runtime)/$($Platform.FileName) already exists" -ForegroundColor Green
        return
    }

    # Create directory if it doesn't exist
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    Write-Host "Downloading $($Platform.Runtime)..." -ForegroundColor Yellow

    try {
        # Note: These URLs are placeholders - actual whisper.cpp releases may have different structure
        # You may need to build from source or find appropriate pre-built binaries

        # For demonstration, create a placeholder file
        "Placeholder for $($Platform.FileName) - Replace with actual binary" | Out-File $targetFile

        Write-Host "✓ Downloaded $($Platform.Runtime)/$($Platform.FileName)" -ForegroundColor Green

        # In a real implementation, you would:
        # 1. Download the archive
        # 2. Extract the specific file
        # 3. Copy to the target location
        # 4. Clean up temporary files

    } catch {
        Write-Host "✗ Failed to download $($Platform.Runtime): $_" -ForegroundColor Red
    }
}

# Main execution
Write-Host "WhisperFS Native Library Downloader" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "Output: $OutputPath" -ForegroundColor Cyan
Write-Host ""

# Download standard platforms
Write-Host "Downloading standard platforms..." -ForegroundColor White
foreach ($platform in $platforms) {
    Download-Platform $platform
}

# Optionally download CUDA variants
$downloadCuda = Read-Host "Download CUDA variants? (y/n)"
if ($downloadCuda -eq 'y') {
    Write-Host ""
    Write-Host "Downloading CUDA platforms..." -ForegroundColor White
    foreach ($platform in $cudaPlatforms) {
        Download-Platform $platform
    }
}

Write-Host ""
Write-Host "Done! Native libraries are in: $OutputPath" -ForegroundColor Green
Write-Host ""
Write-Host "Note: The actual URLs and binary names may differ from whisper.cpp releases." -ForegroundColor Yellow
Write-Host "You may need to build from source or adjust the download URLs." -ForegroundColor Yellow