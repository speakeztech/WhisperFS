namespace WhisperFS.Runtime

open System
open System.Threading
open WhisperFS

/// Model management and downloading
module Models =

    /// Get metadata for all available models
    val getAvailableModels: unit -> ModelMetadata list

    /// Get status of all models (downloaded, downloading, etc.)
    val getAllModelStatus: unit -> (ModelMetadata * ModelStatus) list

    /// Get models that are currently downloaded
    val getDownloadedModels: unit -> (ModelMetadata * string * int64) list

    /// Get models that are not downloaded
    val getNotDownloadedModels: unit -> ModelMetadata list

    /// Check if specific model is downloaded
    val isModelDownloaded: modelType:ModelType -> bool

    /// Get status of specific model
    val getModelStatus: modelType:ModelType -> ModelStatus

    /// Get local path for model
    val getModelPath: modelType:ModelType -> string

    /// Download model with basic progress
    val downloadModelAsync: modelType:ModelType -> Async<Result<string, WhisperError>>

    /// Download model with cancellation support
    val downloadModelWithCancellationAsync: modelType:ModelType -> CancellationToken -> Async<Result<string, WhisperError>>