namespace WhisperFS.Native

/// Public API for native library management
module Library =

    /// Initialize WhisperFS native runtime
    /// This will download and load the best available whisper.cpp binary for your platform
    let initializeAsync() =
        NativeLibraryLoader.initialize()

    /// Ensure a specific runtime is available and downloaded
    let ensureRuntimeAsync runtimeType =
        NativeLibraryLoader.ensureRuntimeAsync runtimeType

    /// Get the path where native libraries are stored
    let getNativeLibraryDirectory() =
        NativeLibraryLoader.getNativeLibraryDir()

    /// Detect which runtimes are available on this system
    let getAvailableRuntimes() =
        NativeLibraryLoader.detectAvailableRuntimes()

    /// Information about the native library loader
    let getVersionInfo() =
        {| WhisperCppVersion = NativeLibraryLoader.WhisperCppVersion
           NativeDirectory = NativeLibraryLoader.getNativeLibraryDir()
           Platform = NativeLibraryLoader.detectPlatform() |}