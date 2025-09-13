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
- **OpenCL**: For AMD, Intel, and other GPUs via CLBlast (~15-20 MB)

#### macOS
- **CoreML**: Hardware-accelerated for Apple Silicon
- **OpenCL**: Available through system frameworks (Intel/AMD GPUs)
- **CPU**: Fallback for Intel Macs

#### Linux
- **CPU**: Standard builds
- **CUDA**: When NVIDIA drivers detected
- **OpenCL**: For AMD, Intel GPUs when OpenCL runtime installed
- (Linux binaries need to be built separately as whisper.cpp doesn't provide them)

## Runtime Selection

The library automatically selects the best runtime based on priority:

1. **CUDA 12** (if NVIDIA GPU + CUDA 12 detected)
2. **CUDA 11** (if NVIDIA GPU + CUDA 11 detected)
3. **CoreML** (on macOS with Apple Silicon)
4. **OpenCL** (if OpenCL runtime detected - AMD, Intel, or other GPUs)
5. **BLAS** (if OpenBLAS or Intel MKL detected)
6. **CPU** (universal fallback)

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

## GPU Acceleration

### OpenCL Support

WhisperFS includes automatic OpenCL support for GPU acceleration on non-NVIDIA hardware:

#### Supported Hardware
- **AMD GPUs**: Radeon RX series, Radeon Pro, AMD Instinct
- **Intel GPUs**: Intel Arc, Intel Iris Xe, Intel UHD Graphics
- **Other**: Any GPU with OpenCL 1.2+ support

#### Detection
The library automatically detects OpenCL availability by checking for:
- **Windows**: `OpenCL.dll` in system directories
- **Linux**: `libOpenCL.so` in standard library paths
- **macOS**: OpenCL framework (built-in)

#### Performance
OpenCL acceleration typically provides:
- 5-10x speedup over CPU-only processing
- 50-70% of CUDA performance on comparable hardware
- Better efficiency than BLAS for long-form audio

### CUDA Support

For NVIDIA GPUs, WhisperFS supports both CUDA 11 and CUDA 12:

#### Requirements
- NVIDIA GPU with Compute Capability 5.0+
- CUDA Toolkit 11.8+ or 12.0+
- Compatible NVIDIA drivers

#### Auto-Detection
The library checks for CUDA by:
1. Examining `CUDA_PATH` environment variable
2. Checking standard CUDA installation directories
3. Verifying driver compatibility

### Verification

To verify GPU acceleration is working:

```fsharp
let runtimes = WhisperFS.Native.Library.detectAvailableRuntimes()
let gpuRuntime = runtimes |> List.tryFind (fun r ->
    match r.Type with
    | RuntimeType.Cuda11 | RuntimeType.Cuda12 | RuntimeType.OpenCL -> true
    | _ -> false)

match gpuRuntime with
| Some runtime ->
    printfn "GPU acceleration available: %A" runtime.Type
| None ->
    printfn "No GPU acceleration detected, using CPU"
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
├── opencl\
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

### Batch Processing (1 hour audio file)

| Runtime | Processing Time | Speed Factor |
|---------|----------------|--------------|
| CUDA 12 | ~30s | 120x |
| CUDA 11 | ~35s | 100x |
| CoreML | ~45s | 80x |
| OpenCL | ~50s | 72x |
| BLAS | ~90s | 40x |
| CPU | ~180s | 20x |

### Realtime Streaming

| Runtime | Latency | Can Process Realtime? |
|---------|---------|---------------------|
| CUDA 12 | <100ms | Yes (with headroom) |
| CUDA 11 | <150ms | Yes (with headroom) |
| CoreML | <200ms | Yes |
| OpenCL | <250ms | Yes |
| BLAS | <500ms | Yes (marginal) |
| CPU | <1000ms | Depends on model size |

Note: Realtime performance requires processing audio chunks faster than they are captured. Latency values are approximate for base model with 1-second chunks.

## Future Enhancements

- [ ] Linux pre-built binaries
- [ ] Android/iOS support via xcframework
- [ ] Custom build configurations
- [ ] Model-specific optimizations
- [ ] Runtime performance profiling