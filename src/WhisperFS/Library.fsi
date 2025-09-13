namespace WhisperFS

open System
open System.Threading

/// Main WhisperFS API - Complete interface with all new functionality
module WhisperFS =

    /// Initialize WhisperFS native libraries (call once at startup)
    val initialize: unit -> Async<Result<unit, WhisperError>>

    /// Create a whisper client with configuration
    val createClient: config:WhisperConfig -> Async<Result<IWhisperClient, WhisperError>>

    /// Create a whisper client from a specific model path
    val createClientFromModel: modelPath:string -> config:WhisperConfig -> Async<Result<IWhisperClient, WhisperError>>

    /// Create a whisper client with custom context parameters
    val createClientWithParams: modelPath:string -> contextParams:WhisperFS.Native.WhisperContextParams -> config:WhisperConfig -> Async<Result<IWhisperClient, WhisperError>>

    /// Download a model if not already present
    val downloadModel: modelType:ModelType -> Async<Result<string, WhisperError>>

    /// Check if a model is already downloaded
    val isModelDownloaded: modelType:ModelType -> bool

    /// Get the local path for a model
    val getModelPath: modelType:ModelType -> string


