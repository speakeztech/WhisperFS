namespace WhisperFS.Runtime

open WhisperFS

/// System capability detection for optimal runtime selection
module RuntimeDetection =

    /// Hardware acceleration capability detection
    val hasCudaSupport: unit -> bool
    val getCudaVersion: unit -> string option
    val hasAvxSupport: unit -> bool
    val hasOpenBlasSupport: unit -> bool
    val hasMklSupport: unit -> bool
    val hasCoreMLSupport: unit -> bool
    val getAvailableMemoryGB: unit -> float

    /// Complete system capability assessment
    type RuntimeCapabilities = {
        HasCuda: bool
        CudaVersion: string option
        HasAvx: bool
        HasBlas: bool
        HasCoreML: bool
        AvailableMemoryGB: float
        ProcessorCount: int
    }

    /// Analyze current system capabilities
    val detectCapabilities: unit -> RuntimeCapabilities

    /// Get recommended model based on system performance
    val getRecommendedModel: RuntimeCapabilities -> ModelType

    /// Get optimal native runtime for current system
    val getOptimalRuntime: RuntimeCapabilities -> string