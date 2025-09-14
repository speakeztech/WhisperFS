namespace WhisperFS.Native

open System
open WhisperFS

/// Native library management for whisper.cpp binaries
module NativeLibraryLoader =

    /// Runtime types with performance characteristics
    type RuntimeType =
        | Cpu              // Standard CPU version
        | Blas             // BLAS-accelerated CPU
        | Cuda11           // CUDA 11.8.0
        | Cuda12           // CUDA 12.4.0
        | CoreML           // macOS CoreML
        | OpenCL           // OpenCL GPU
        | Vulkan           // Vulkan GPU

    /// Platform detection for native library selection
    type Platform =
        | Windows of arch:NativeArchitecture
        | Linux of arch:NativeArchitecture
        | MacOS of arch:NativeArchitecture

    and NativeArchitecture =
        | X86
        | X64
        | Arm64

    /// Runtime information with download capabilities
    type RuntimeInfo = {
        Type: RuntimeType
        Platform: Platform
        Priority: int
        FileName: string
        DownloadUrl: string option
        Available: bool
    }

    /// Get native library storage directory
    val getNativeLibraryDir: unit -> string

    /// Detect current platform and architecture
    val detectPlatform: unit -> Platform

    /// Get download URL for specific runtime and platform
    val getDownloadUrl: RuntimeType -> Platform -> string option

    /// Get expected library filename for platform
    val getLibraryFileName: Platform -> string

    /// Check system capability for CUDA acceleration
    val hasCudaSupport: unit -> bool

    /// Check system capability for BLAS acceleration
    val hasBlasSupport: unit -> bool

    /// Check system capability for AVX instructions
    val hasAvxSupport: unit -> bool

    /// Detect all available runtimes based on system capabilities
    val detectAvailableRuntimes: unit -> RuntimeInfo list

    /// Download and extract runtime library with progress
    val downloadRuntimeAsync: RuntimeInfo -> Async<Result<string, WhisperError>>

    /// Load the best available runtime automatically
    val loadBestRuntimeAsync: unit -> Async<Result<RuntimeInfo, WhisperError>>

    /// Ensure specific runtime is downloaded and available
    val ensureRuntimeAsync: RuntimeType -> Async<Result<string, WhisperError>>

    /// Get filesystem path to runtime library
    val getRuntimePath: RuntimeType -> string

    /// Initialize native library resolver (call once at startup)
    val initialize: unit -> Async<Result<RuntimeInfo, WhisperError>>