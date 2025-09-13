namespace WhisperFS.Native

open System
open System.IO
open System.Net.Http
open System.Runtime.InteropServices
open System.IO.Compression
open WhisperFS

/// Native library management - downloads and loads whisper.cpp binaries
module NativeLibraryLoader =

    /// Runtime types available from whisper.cpp releases
    type RuntimeType =
        | Cpu              // Standard CPU version
        | Blas             // BLAS-accelerated CPU
        | Cuda11           // CUDA 11.8.0
        | Cuda12           // CUDA 12.4.0
        | CoreML           // macOS CoreML (from xcframework)
        | OpenCL           // OpenCL GPU (AMD, Intel, others)
        | Vulkan           // Vulkan GPU

    type Platform =
        | Windows of arch:NativeArchitecture
        | Linux of arch:NativeArchitecture
        | MacOS of arch:NativeArchitecture

    and NativeArchitecture =
        | X86
        | X64
        | Arm64

    type RuntimeInfo = {
        Type: RuntimeType
        Platform: Platform
        Priority: int
        FileName: string
        DownloadUrl: string option
        Available: bool
    }

    /// Base URL for whisper.cpp GitHub releases
    let [<Literal>] ReleaseBaseUrl = "https://github.com/ggerganov/whisper.cpp/releases/download"

    /// Pinned version for stability
    let [<Literal>] WhisperCppVersion = "v1.7.6"

    /// Get the native library directory
    let getNativeLibraryDir() =
        let baseDir =
            match Environment.GetEnvironmentVariable("WHISPERFS_NATIVE_DIR") with
            | null | "" ->
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WhisperFS",
                    "native",
                    WhisperCppVersion)
            | dir -> dir
        Directory.CreateDirectory(baseDir) |> ignore
        baseDir

    /// Detect current platform
    let detectPlatform() =
        let arch =
            match RuntimeInformation.ProcessArchitecture with
            | Architecture.X86 -> NativeArchitecture.X86
            | Architecture.X64 -> NativeArchitecture.X64
            | Architecture.Arm | Architecture.Arm64 -> NativeArchitecture.Arm64
            | _ -> NativeArchitecture.X64 // Default to x64

        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Windows arch
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
            Linux arch
        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
            MacOS arch
        else
            Windows NativeArchitecture.X64 // Default

    /// Get download URL for a specific runtime
    let getDownloadUrl (runtime: RuntimeType) (platform: Platform) =
        match platform, runtime with
        | Windows (NativeArchitecture.X64), Cpu ->
            Some $"{ReleaseBaseUrl}/{WhisperCppVersion}/whisper-bin-x64.zip"
        | Windows (NativeArchitecture.X86), Cpu ->
            Some $"{ReleaseBaseUrl}/{WhisperCppVersion}/whisper-bin-Win32.zip"
        | Windows (NativeArchitecture.X64), Blas ->
            Some $"{ReleaseBaseUrl}/{WhisperCppVersion}/whisper-blas-bin-x64.zip"
        | Windows (NativeArchitecture.X86), Blas ->
            Some $"{ReleaseBaseUrl}/{WhisperCppVersion}/whisper-blas-bin-Win32.zip"
        | Windows (NativeArchitecture.X64), Cuda11 ->
            Some $"{ReleaseBaseUrl}/{WhisperCppVersion}/whisper-cublas-11.8.0-bin-x64.zip"
        | Windows (NativeArchitecture.X64), Cuda12 ->
            Some $"{ReleaseBaseUrl}/{WhisperCppVersion}/whisper-cublas-12.4.0-bin-x64.zip"
        | Windows (NativeArchitecture.X64), OpenCL ->
            Some $"{ReleaseBaseUrl}/{WhisperCppVersion}/whisper-clblast-bin-x64.zip"
        | Linux (NativeArchitecture.X64), OpenCL ->
            Some $"{ReleaseBaseUrl}/{WhisperCppVersion}/whisper-clblast-bin-linux-x64.tar.gz"
        | MacOS _, CoreML ->
            Some $"{ReleaseBaseUrl}/{WhisperCppVersion}/whisper-v{WhisperCppVersion}-xcframework.zip"
        | _ -> None

    /// Get the expected library file name
    let getLibraryFileName (platform: Platform) =
        match platform with
        | Windows _ -> "whisper.dll"
        | Linux _ -> "libwhisper.so"
        | MacOS _ -> "libwhisper.dylib"

    /// Check if CUDA is available
    let hasCudaSupport() =
        try
            match Environment.GetEnvironmentVariable("CUDA_PATH") with
            | null | "" -> false
            | _ ->
                // Check for CUDA 12 first, then 11
                let cuda12Path = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.4\bin"
                let cuda11Path = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v11.8\bin"
                Directory.Exists(cuda12Path) || Directory.Exists(cuda11Path)
        with _ -> false

    /// Check if BLAS is available
    let hasBlasSupport() =
        // Check for OpenBLAS or Intel MKL
        Environment.GetEnvironmentVariable("OPENBLAS_PATH") <> null ||
        Environment.GetEnvironmentVariable("MKLROOT") <> null

    /// Check for OpenCL support (AMD, Intel, and other GPUs)
    let hasOpenCLSupport() =
        try
            // Check for OpenCL runtime
            if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
                // Windows: Check for OpenCL.dll in system directories
                File.Exists(@"C:\Windows\System32\OpenCL.dll") ||
                File.Exists(@"C:\Windows\SysWOW64\OpenCL.dll")
            elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
                // Linux: Check for libOpenCL.so
                File.Exists("/usr/lib/x86_64-linux-gnu/libOpenCL.so.1") ||
                File.Exists("/usr/lib/libOpenCL.so.1") ||
                File.Exists("/usr/lib64/libOpenCL.so.1")
            elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
                // macOS: OpenCL framework is usually present
                Directory.Exists("/System/Library/Frameworks/OpenCL.framework")
            else
                false
        with _ -> false

    /// Check for AVX support
    let hasAvxSupport() =
        try
            // This is a simplified check - proper implementation would use CPUID
            true // Most modern CPUs have AVX
        with _ -> false

    /// Detect available runtimes based on system capabilities
    let detectAvailableRuntimes() =
        let platform = detectPlatform()
        let libraryName = getLibraryFileName platform

        [
            // Priority 1: CUDA for NVIDIA GPUs (Windows/Linux)
            if (platform = Windows (NativeArchitecture.X64) ||
                match platform with Linux (NativeArchitecture.X64) -> true | _ -> false) &&
               hasCudaSupport() then
                // Prefer CUDA 12 over CUDA 11
                { Type = Cuda12; Platform = platform; Priority = 1;
                  FileName = libraryName;
                  DownloadUrl = getDownloadUrl Cuda12 platform; Available = true }
                { Type = Cuda11; Platform = platform; Priority = 2;
                  FileName = libraryName;
                  DownloadUrl = getDownloadUrl Cuda11 platform; Available = true }

            // Priority 1: CoreML for macOS
            match platform with
            | MacOS _ ->
                { Type = CoreML; Platform = platform; Priority = 1;
                  FileName = libraryName;
                  DownloadUrl = getDownloadUrl CoreML platform; Available = true }
            | _ -> ()

            // Priority 3: OpenCL for AMD, Intel, and other GPUs
            if hasOpenCLSupport() then
                { Type = OpenCL; Platform = platform; Priority = 3;
                  FileName = libraryName;
                  DownloadUrl = getDownloadUrl OpenCL platform; Available = true }

            // Priority 4: BLAS acceleration (CPU)
            if hasBlasSupport() then
                { Type = Blas; Platform = platform; Priority = 4;
                  FileName = libraryName;
                  DownloadUrl = getDownloadUrl Blas platform; Available = true }

            // Priority 10: Always include CPU fallback
            { Type = Cpu; Platform = platform; Priority = 10;
              FileName = libraryName;
              DownloadUrl = getDownloadUrl Cpu platform; Available = true }
        ]
        |> List.sortBy (fun r -> r.Priority)

    /// Download and extract a runtime
    let downloadRuntimeAsync (runtime: RuntimeInfo) = async {
        match runtime.DownloadUrl with
        | None ->
            return Error (WhisperError.NativeLibraryError
                $"No download URL available for {runtime.Type} on {runtime.Platform}")
        | Some url ->
            try
                let nativeDir = getNativeLibraryDir()
                let runtimeDir = Path.Combine(nativeDir, runtime.Type.ToString().ToLower())
                Directory.CreateDirectory(runtimeDir) |> ignore

                let libraryPath = Path.Combine(runtimeDir, runtime.FileName)

                // Check if already downloaded
                if File.Exists(libraryPath) then
                    return Ok libraryPath
                else
                    // Download the zip file
                    use client = new HttpClient()
                    client.DefaultRequestHeaders.Add("User-Agent", "WhisperFS/1.0")

                    let! response = client.GetAsync(url) |> Async.AwaitTask
                    response.EnsureSuccessStatusCode() |> ignore

                    let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask

                    // Save and extract
                    let zipPath = Path.Combine(runtimeDir, "temp.zip")
                    File.WriteAllBytes(zipPath, bytes)

                    try
                        // Extract the zip
                        ZipFile.ExtractToDirectory(zipPath, runtimeDir, true)

                        // Find the whisper.dll in extracted files
                        let extractedDll =
                            Directory.GetFiles(runtimeDir, runtime.FileName, SearchOption.AllDirectories)
                            |> Array.tryHead

                        match extractedDll with
                        | Some dll when dll <> libraryPath ->
                            // Move to expected location
                            File.Move(dll, libraryPath, true)
                        | _ -> ()

                        // Clean up zip
                        File.Delete(zipPath)

                        if File.Exists(libraryPath) then
                            return Ok libraryPath
                        else
                            return Error (WhisperError.NativeLibraryError
                                $"Failed to extract {runtime.FileName} from download")
                    with ex ->
                        return Error (WhisperError.NativeLibraryError
                            $"Failed to extract runtime: {ex.Message}")

            with ex ->
                return Error (WhisperError.NetworkError
                    $"Failed to download runtime from {url}: {ex.Message}")
    }

    /// Load the best available runtime
    let loadBestRuntimeAsync() = async {
        let runtimes = detectAvailableRuntimes()

        // Try runtimes in priority order
        let rec tryLoad (runtimes: RuntimeInfo list) =
            async {
                match runtimes with
                | [] ->
                    return Error (WhisperError.NativeLibraryError
                        "No compatible runtime found")
                | runtime::rest ->
                    // Try to download if needed
                    let! downloadResult = downloadRuntimeAsync runtime

                    match downloadResult with
                    | Ok libraryPath ->
                        try
                            // Try to load the library
                            let mutable handle = IntPtr.Zero
                            let loaded = NativeLibrary.TryLoad(libraryPath, &handle)
                            if loaded then
                                printfn $"Loaded WhisperFS native runtime: {runtime.Type} from {libraryPath}"
                                return Ok runtime
                            else
                                // Try next runtime
                                return! tryLoad rest
                        with ex ->
                            // Log error and try next runtime
                            printfn $"Failed to load {runtime.Type}: {ex.Message}"
                            return! tryLoad rest
                    | Error _ ->
                        // Try next runtime
                        return! tryLoad rest
            }

        return! tryLoad runtimes
    }

    /// Ensure a specific runtime is available
    let ensureRuntimeAsync (runtimeType: RuntimeType) = async {
        let platform = detectPlatform()
        let runtime = {
            Type = runtimeType
            Platform = platform
            Priority = 1
            FileName = getLibraryFileName platform
            DownloadUrl = getDownloadUrl runtimeType platform
            Available = true
        }

        let! result = downloadRuntimeAsync runtime
        match result with
        | Ok path -> return Ok path
        | Error err -> return Error err
    }

    /// Get path to runtime library
    let getRuntimePath (runtimeType: RuntimeType) =
        let platform = detectPlatform()
        let nativeDir = getNativeLibraryDir()
        let runtimeDir = Path.Combine(nativeDir, runtimeType.ToString().ToLower())
        let libraryName = getLibraryFileName platform
        Path.Combine(runtimeDir, libraryName)

    /// Initialize native library (call once at startup)
    let initialize() = async {
        // Set native library resolver
        NativeLibrary.SetDllImportResolver(typeof<WhisperFullParams>.Assembly,
            DllImportResolver(fun libraryName assembly searchPath ->
                if libraryName = WhisperNative.LibraryName ||
                   libraryName.Contains("whisper") then
                    // Try to get the best runtime
                    let runtime =
                        detectAvailableRuntimes()
                        |> List.tryHead

                    match runtime with
                    | Some r ->
                        let path = getRuntimePath r.Type
                        if File.Exists(path) then
                            NativeLibrary.Load(path)
                        else
                            IntPtr.Zero
                    | None -> IntPtr.Zero
                else
                    IntPtr.Zero
            ))

        // Try to load the best runtime
        return! loadBestRuntimeAsync()
    }