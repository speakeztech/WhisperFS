# WhisperFS Runtime Strategy

## Overview

WhisperFS uses a **dynamic runtime loading strategy** that differs significantly from traditional NuGet package approaches. Instead of bundling native libraries in NuGet packages, WhisperFS downloads the optimal native library and models at runtime based on system capabilities.

## Key Differences from Static Approaches

### Traditional Approach (e.g., Furnace/TorchSharp)
- Native libraries bundled in NuGet packages
- Multiple runtime-specific packages (e.g., `.runtime.win-x64`, `.runtime.linux-x64`)
- Large package sizes
- Fixed at build/install time

### WhisperFS Dynamic Approach
- Lightweight NuGet packages (F# code only)
- Native libraries downloaded on first use
- Models downloaded on demand
- Runtime selection based on actual system capabilities
- Smaller initial download

## Runtime Selection Logic

```
1. Detect System Capabilities
   ├── CUDA (versions 11/12)
   ├── CoreML (macOS)
   ├── AVX/AVX2 instructions
   ├── BLAS (OpenBLAS/Intel MKL)
   └── Available memory

2. Priority Order (highest to lowest)
   ├── CUDA 12 (if available)
   ├── CUDA 11 (if available)
   ├── CoreML (macOS with Apple Silicon)
   ├── BLAS-accelerated
   ├── AVX-optimized CPU
   └── Generic CPU (fallback)

3. Download Location
   └── %LOCALAPPDATA%\WhisperFS\native\{version}\{runtime}
       └── Or $WHISPERFS_NATIVE_DIR if set
```

## Model Management

Models are downloaded separately from Hugging Face:
- **Location**: `%LOCALAPPDATA%\WhisperFS\models`
- **On-demand**: Only downloaded when requested
- **Progress tracking**: Full download progress events
- **Verification**: Optional SHA256 verification
- **Size-aware**: Recommends models based on available memory

## Benefits

1. **Optimal Performance**: Always uses the best available runtime for the system
2. **Smaller Packages**: NuGet packages contain only F# code
3. **Flexible Deployment**: Can upgrade native libraries without redeploying app
4. **GPU Auto-detection**: Automatically uses GPU acceleration when available
5. **Model Choice**: Users can choose models based on their needs

## Environment Variables

- `WHISPERFS_NATIVE_DIR`: Override native library directory
- `WHISPERFS_MODELS_DIR`: Override models directory
- `CUDA_PATH`: Auto-detected for CUDA support
- `OPENBLAS_PATH`: Auto-detected for BLAS support
- `MKLROOT`: Auto-detected for Intel MKL support

## CI/CD Considerations

The GitHub Actions workflow:
1. Does NOT bundle native libraries
2. Tests runtime download mechanism
3. Publishes lightweight NuGet packages
4. Caches downloaded runtimes for faster CI

## Usage Example

```fsharp
// WhisperFS automatically downloads the best runtime
let! runtime = NativeLibraryLoader.initialize() |> Async.RunSynchronously

// Models downloaded on first use
let! modelPath = ModelDownloader.downloadModelAsync ModelType.Base CancellationToken.None

// Everything handled transparently
let whisper = WhisperBuilder.Create()
                .WithModel(ModelType.Base)
                .Build()
```

## Package Structure

```
WhisperFS (meta-package)
├── WhisperFS.Core (types, interfaces)
├── WhisperFS.Native (P/Invoke, runtime loader)
└── WhisperFS.Runtime (model downloader, detection)
```

None of these packages contain native binaries - they're all pure F# assemblies that handle downloading at runtime.