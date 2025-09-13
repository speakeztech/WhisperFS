namespace WhisperFS.Native

open WhisperFS

/// Native whisper.cpp integration with runtime management
module Library =

    /// Initialize native library with automatic runtime detection
    val initializeAsync: unit -> Async<Result<NativeLibraryLoader.RuntimeInfo, WhisperError>>

    /// Ensure specific runtime is downloaded and available
    val ensureRuntimeAsync: runtimeType:NativeLibraryLoader.RuntimeType -> Async<Result<string, WhisperError>>

    /// Get native library storage directory
    val getNativeLibraryDirectory: unit -> string

    /// Get all available runtimes for current system
    val getAvailableRuntimes: unit -> NativeLibraryLoader.RuntimeInfo list

    /// Get version and platform information
    val getVersionInfo: unit -> {| WhisperCppVersion: string; NativeDirectory: string; Platform: NativeLibraryLoader.Platform |}