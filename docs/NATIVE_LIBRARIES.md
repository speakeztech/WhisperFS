# Native Library Management

## Overview

WhisperFS uses a "batteries included" approach for native whisper.cpp binaries, automatically downloading and managing platform-specific libraries as needed.

## Strategy

### Automatic Download
- Native libraries are downloaded from official whisper.cpp GitHub releases
- Currently pinned to whisper.cpp v1.7.6 for stability
- Libraries are cached locally to avoid repeated downloads
- Location: `%LOCALAPPDATA%\WhisperFS\native\v1.7.6\` (Windows)

### Platform Support

#### Windows
- **CPU**: Standard x86/x64 binaries (~3-4 MB)
- **BLAS**: OpenBLAS-accelerated binaries (~10-16 MB)
- **CUDA 11.8**: For older NVIDIA GPUs (~44 MB)
- **CUDA 12.4**: For newer NVIDIA GPUs (~443 MB)

#### macOS
- **CoreML**: Hardware-accelerated for Apple Silicon
- **CPU**: Fallback for Intel Macs

#### Linux
- **CPU**: Standard builds
- **CUDA**: When NVIDIA drivers detected
- (Linux binaries need to be built separately as whisper.cpp doesn't provide them)

## Runtime Selection

The library automatically selects the best runtime based on priority:

1. **CUDA 12** (if NVIDIA GPU + CUDA 12 detected)
2. **CUDA 11** (if NVIDIA GPU + CUDA 11 detected)
3. **BLAS** (if OpenBLAS or Intel MKL detected)
4. **CoreML** (on macOS with Apple Silicon)
5. **CPU** (universal fallback)

## Usage

### Automatic (Recommended)

```fsharp
// Initialize WhisperFS - downloads best runtime automatically
let! result = WhisperFS.Native.Library.initializeAsync()

match result with
| Ok runtime ->
    printfn "Loaded runtime: %A" runtime.Type
| Error err ->
    printfn "Failed to initialize: %s" err.Message
```

### Manual Runtime Selection

```fsharp
// Force a specific runtime
let! result = WhisperFS.Native.Library.ensureRuntimeAsync RuntimeType.Cuda12

match result with
| Ok path ->
    printfn "CUDA 12 runtime available at: %s" path
| Error err ->
    printfn "Failed to load CUDA runtime: %s" err.Message
```

### Check Available Runtimes

```fsharp
let runtimes = WhisperFS.Native.Library.getAvailableRuntimes()

for runtime in runtimes do
    printfn "%A - Priority %d - %s"
        runtime.Type
        runtime.Priority
        (if runtime.Available then "Available" else "Not available")
```

## Environment Variables

### WHISPERFS_NATIVE_DIR
Override the default native library directory:
```bash
set WHISPERFS_NATIVE_DIR=C:\MyApp\native
```

### CUDA_PATH
Automatically detected to enable CUDA support:
```bash
set CUDA_PATH=C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.4
```

### OPENBLAS_PATH / MKLROOT
Detected for BLAS acceleration:
```bash
set OPENBLAS_PATH=C:\Tools\OpenBLAS
```

## File Structure

After initialization, the native directory contains:

```
%LOCALAPPDATA%\WhisperFS\native\v1.7.6\
├── cuda12\
│   └── whisper.dll
├── cuda11\
│   └── whisper.dll
├── blas\
│   └── whisper.dll
└── cpu\
    └── whisper.dll
```

## Versioning

- WhisperFS is pinned to specific whisper.cpp versions for stability
- Version updates are tested before being adopted
- Multiple versions can coexist in different directories

## Troubleshooting

### "Native library not found"
- Ensure internet connection for first-time download
- Check firewall doesn't block GitHub access
- Verify write permissions to `%LOCALAPPDATA%\WhisperFS`

### "CUDA runtime failed to load"
- Verify CUDA toolkit is installed
- Check GPU driver version matches CUDA version
- Try falling back to CPU runtime

### Manual Download
If automatic download fails, manually download from:
https://github.com/ggerganov/whisper.cpp/releases/v1.7.6

Extract the appropriate zip file to:
`%LOCALAPPDATA%\WhisperFS\native\v1.7.6\[runtime_type]\`

## Distribution

For deployment scenarios:

### Option 1: Runtime Download (Recommended)
- Ship only F# assemblies
- Native libraries downloaded on first run
- Smaller initial package size

### Option 2: Bundled Binaries
- Include native libraries in package
- Set `WHISPERFS_NATIVE_DIR` to package location
- Larger package but no runtime download needed

### Option 3: NuGet Runtime Packages
Similar to Whisper.NET approach:
- `WhisperFS.Runtime.Cuda` - CUDA binaries
- `WhisperFS.Runtime.Cpu` - CPU binaries
- User chooses which to install

## Performance Comparison

Approximate performance on 1 hour of audio:

| Runtime | Time | Relative Speed |
|---------|------|----------------|
| CUDA 12 | ~30s | 120x realtime |
| CUDA 11 | ~35s | 100x realtime |
| BLAS | ~90s | 40x realtime |
| CoreML | ~45s | 80x realtime |
| CPU | ~180s | 20x realtime |

## Future Enhancements

- [ ] Linux pre-built binaries
- [ ] Android/iOS support via xcframework
- [ ] Custom build configurations
- [ ] Model-specific optimizations
- [ ] Runtime performance profiling