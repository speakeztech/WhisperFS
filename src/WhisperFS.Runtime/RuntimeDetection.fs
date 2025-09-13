namespace WhisperFS.Runtime

open System
open System.Runtime.InteropServices
open WhisperFS

/// Runtime detection for optimal native library selection
module RuntimeDetection =

    /// Check if CUDA is available
    let hasCudaSupport() =
        // Check for CUDA environment variable
        let cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH")
        not (String.IsNullOrEmpty(cudaPath))

    /// Check CUDA version
    let getCudaVersion() =
        let cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH")
        if String.IsNullOrEmpty(cudaPath) then
            None
        else
            // Try to parse version from path (e.g., "CUDA\v12.0")
            if cudaPath.Contains("v12") || cudaPath.Contains("12.") then
                Some "12"
            elif cudaPath.Contains("v11") || cudaPath.Contains("11.") then
                Some "11"
            else
                None

    /// Check if AVX instructions are supported
    let hasAvxSupport() =
        try
            // This is a simplified check - in production you'd use CPUID
            RuntimeInformation.ProcessArchitecture = Architecture.X64 ||
            RuntimeInformation.ProcessArchitecture = Architecture.X86
        with
        | _ -> false

    /// Check if OpenBLAS is available
    let hasOpenBlasSupport() =
        let openBlasPath = Environment.GetEnvironmentVariable("OPENBLAS_PATH")
        not (String.IsNullOrEmpty(openBlasPath))

    /// Check if Intel MKL is available
    let hasMklSupport() =
        let mklRoot = Environment.GetEnvironmentVariable("MKLROOT")
        not (String.IsNullOrEmpty(mklRoot))

    /// Check if CoreML is available (macOS)
    let hasCoreMLSupport() =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
        (RuntimeInformation.ProcessArchitecture = Architecture.Arm64 ||
         RuntimeInformation.ProcessArchitecture = Architecture.X64)

    /// Get available system memory in GB
    let getAvailableMemoryGB() =
        try
            // This is platform-specific; simplified version
            let totalMemory = GC.GetTotalMemory(false)
            float (totalMemory / (1024L * 1024L * 1024L))
        with
        | _ -> 4.0 // Default assumption

    /// Runtime capability information
    type RuntimeCapabilities = {
        HasCuda: bool
        CudaVersion: string option
        HasAvx: bool
        HasBlas: bool
        HasCoreML: bool
        AvailableMemoryGB: float
        ProcessorCount: int
    }

    /// Detect current system capabilities
    let detectCapabilities() = {
        HasCuda = hasCudaSupport()
        CudaVersion = getCudaVersion()
        HasAvx = hasAvxSupport()
        HasBlas = hasOpenBlasSupport() || hasMklSupport()
        HasCoreML = hasCoreMLSupport()
        AvailableMemoryGB = getAvailableMemoryGB()
        ProcessorCount = Environment.ProcessorCount
    }

    /// Get recommended model based on system capabilities
    let getRecommendedModel (capabilities: RuntimeCapabilities) =
        match capabilities with
        | { HasCuda = true; AvailableMemoryGB = mem } when mem >= 6.0 ->
            ModelType.LargeV3
        | { HasCuda = true; AvailableMemoryGB = mem } when mem >= 4.0 ->
            ModelType.Medium
        | { HasCoreML = true; AvailableMemoryGB = mem } when mem >= 4.0 ->
            ModelType.Medium
        | { AvailableMemoryGB = mem } when mem >= 2.0 ->
            ModelType.Small
        | { AvailableMemoryGB = mem } when mem >= 1.0 ->
            ModelType.Base
        | _ ->
            ModelType.Tiny

    /// Get optimal runtime based on capabilities
    let getOptimalRuntime (capabilities: RuntimeCapabilities) =
        match capabilities with
        | { HasCuda = true; CudaVersion = Some "12" } -> "cuda12"
        | { HasCuda = true; CudaVersion = Some "11" } -> "cuda11"
        | { HasCuda = true } -> "cuda12" // Default to newer version
        | { HasCoreML = true } -> "coreml"
        | { HasBlas = true } -> "blas"
        | { HasAvx = true } -> "cpu"
        | _ -> "cpu_noavx"