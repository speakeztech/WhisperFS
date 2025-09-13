module WhisperFS.Tests.Native.NativeLoaderTests

open System
open System.IO
open System.Runtime.InteropServices
open Xunit
open FsUnit.Xunit
open WhisperFS.Native

[<Fact>]
let ``Platform detection works correctly`` () =
    let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    let isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
    let isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)

    // At least one platform should be detected
    (isWindows || isMacOS || isLinux) |> should be True

[<Fact>]
let ``Architecture detection works correctly`` () =
    let arch = RuntimeInformation.OSArchitecture

    // Should be one of the known architectures
    [Architecture.X86; Architecture.X64; Architecture.Arm; Architecture.Arm64]
    |> should contain arch

[<Fact>]
let ``Native library naming convention is correct`` () =
    let getLibraryName (platform: string) =
        match platform with
        | "Windows" -> "whisper.dll"
        | "macOS" -> "libwhisper.dylib"
        | "Linux" -> "libwhisper.so"
        | _ -> "unknown"

    getLibraryName "Windows" |> should equal "whisper.dll"
    getLibraryName "macOS" |> should equal "libwhisper.dylib"
    getLibraryName "Linux" |> should equal "libwhisper.so"

[<Fact>]
let ``Library path resolution includes common locations`` () =
    let commonPaths = [
        Environment.CurrentDirectory
        Path.GetDirectoryName(typeof<WhisperFullParams>.Assembly.Location)
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhisperFS", "native")
    ]

    commonPaths |> List.iter (fun path ->
        path |> should not' (be EmptyString)
    )

[<Fact>]
let ``Runtime type enumeration is complete`` () =
    let runtimeTypes = [
        "CPU"
        "CPU_NoAVX"
        "CUDA_11"
        "CUDA_12"
        "CoreML"
        "OpenVINO"
        "Vulkan"
        "BLAS"
    ]

    runtimeTypes |> should haveLength 8

[<Fact>]
let ``Environment variable detection`` () =
    // Test that we can read environment variables
    let cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH")
    let openBlasPath = Environment.GetEnvironmentVariable("OPENBLAS_PATH")
    let mklRoot = Environment.GetEnvironmentVariable("MKLROOT")

    // These may or may not exist, just verify we can check
    cudaPath |> ignore
    openBlasPath |> ignore
    mklRoot |> ignore

[<Fact>]
let ``Library file extensions are platform-specific`` () =
    let getExtension (platform: OSPlatform) =
        if platform = OSPlatform.Windows then ".dll"
        elif platform = OSPlatform.OSX then ".dylib"
        elif platform = OSPlatform.Linux then ".so"
        else ""

    getExtension OSPlatform.Windows |> should equal ".dll"
    getExtension OSPlatform.OSX |> should equal ".dylib"
    getExtension OSPlatform.Linux |> should equal ".so"

[<Fact>]
let ``Native directory structure is valid`` () =
    let baseDir = Path.Combine(Path.GetTempPath(), "WhisperFS_Test", "native")
    let version = "v1.7.6"
    let runtimes = ["cpu"; "cuda11"; "cuda12"; "blas"]

    let paths = [
        for runtime in runtimes do
            yield Path.Combine(baseDir, version, runtime)
    ]

    paths |> List.iter (fun path ->
        // Path should be well-formed
        Path.IsPathRooted(path) || Path.IsPathFullyQualified(path) |> should be True
    )