# WhisperFS Build Infrastructure

## Overview

WhisperFS uses a sophisticated build infrastructure that supports its unique **dynamic runtime loading** strategy. Unlike traditional approaches that bundle native libraries in NuGet packages, WhisperFS downloads optimal native libraries and models at runtime.

## Key Components

### 1. Directory.Build.props
Central configuration for all projects:
- Package versions (FSharp.Core, System.Reactive, etc.)
- NuGet metadata and versioning strategy
- Runtime capability detection flags for testing
- F# compiler warnings configuration

Key features:
- Semantic versioning with preview builds
- GitHub Actions integration
- Source Link support for debugging

### 2. GitHub Actions Workflows

#### build-test-publish.yml
Main CI/CD pipeline:
- Multi-platform testing (Windows, Linux, macOS)
- Lightweight NuGet package creation (no native binaries)
- Automatic publishing on version tags
- Runtime download caching for CI performance

#### pull-request.yml
PR validation:
- Code formatting checks with Fantomas
- FSharpLint analysis
- Build warnings as errors
- Test execution

### 3. Runtime Strategy

WhisperFS packages are **lightweight** - they contain only F# assemblies:
- `WhisperFS.Core` - Types and interfaces
- `WhisperFS.Native` - P/Invoke and runtime loader
- `WhisperFS.Runtime` - Model downloader and detection
- `WhisperFS` - Main API and streaming

Native libraries are downloaded at runtime from:
- whisper.cpp GitHub releases
- Selected based on system capabilities (CUDA, AVX, BLAS, CoreML)
- Cached in `%LOCALAPPDATA%\WhisperFS\native`

Models are downloaded on-demand from:
- Hugging Face (ggerganov/whisper.cpp)
- Cached in `%LOCALAPPDATA%\WhisperFS\models`

## Building Locally

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Create NuGet packages
dotnet pack --configuration Release
```

## Publishing

### Manual Publishing
```bash
# Pack all projects
dotnet pack --configuration Release

# Push to NuGet.org
dotnet nuget push bin/packages/*.nupkg --source https://api.nuget.org/v3/index.json --api-key YOUR_KEY
```

### Automatic Publishing
1. Tag version in git: `git tag v0.2.0`
2. Push tag: `git push origin v0.2.0`
3. GitHub Actions will automatically:
   - Build all platforms
   - Run tests
   - Create NuGet packages
   - Publish to NuGet.org
   - Create GitHub release

## Environment Variables

- `WHISPERFS_NATIVE_DIR` - Override native library directory
- `WHISPERFS_MODELS_DIR` - Override models directory
- `WHISPERFS_TESTGPU` - Enable GPU tests in CI
- `GITHUB_ACTIONS` - Detected in CI for versioning

## Package Versioning

- Local builds: `0.1.0-local-YYMMDD`
- CI preview builds: `0.1.0-preview-{build-number}`
- Release builds: `0.1.0` (from git tags)

## GPU Support

WhisperFS automatically detects and uses:
- CUDA 11/12 on Windows/Linux
- CoreML on macOS
- Vulkan (experimental)
- BLAS acceleration (OpenBLAS/Intel MKL)

## Comparison with Furnace

| Aspect | WhisperFS | Furnace |
|--------|-----------|---------|
| Native deps | Runtime download | NuGet bundled |
| Package size | Small (~100KB) | Large (includes libs) |
| Runtime selection | Dynamic | Fixed at install |
| Model management | On-demand download | N/A |
| GPU detection | Automatic | Build-time |

## Testing

Tests run without requiring pre-downloaded native libraries:
- Runtime loader is tested with mocked downloads
- Integration tests can download actual libraries
- GPU tests enabled with `WHISPERFS_TESTGPU=true`

## Notes

- No native libraries are bundled in packages
- First run will download appropriate runtime (~50-200MB)
- Models downloaded separately on first use (~39MB-3GB)
- All downloads are cached for subsequent runs