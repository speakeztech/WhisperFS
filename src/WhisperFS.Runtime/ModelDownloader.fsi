namespace WhisperFS.Runtime

open System
open WhisperFS

/// Model downloading and integrity verification
module ModelDownloader =

    /// Get metadata for all available models
    val getModelMetadata: unit -> ModelMetadata list

    /// Get metadata for specific model
    val getModelInfo: ModelType -> ModelMetadata option

    /// Download model with progress tracking
    val downloadModelAsync: ModelType -> IProgress<DownloadProgress> option -> Async<Result<string, WhisperError>>

    /// Verify downloaded model integrity
    val verifyModelAsync: ModelType -> string -> Async<Result<bool, WhisperError>>

    /// Get local path for model storage
    val getModelPath: ModelType -> string

    /// Check if model is already downloaded
    val isModelDownloaded: ModelType -> bool

    /// Get downloaded model status and path
    val getModelStatus: ModelType -> ModelStatus

    /// Delete downloaded model from storage
    val deleteModelAsync: ModelType -> Async<Result<unit, WhisperError>>

    /// Get total size of all downloaded models
    val getTotalDownloadedSize: unit -> int64

    /// Clean up incomplete downloads
    val cleanupIncompleteDownloads: unit -> Async<Result<string list, WhisperError>>

    /// Model events for progress monitoring
    val ModelEvents: IObservable<ModelEvent>

    /// Cancel ongoing download for specific model
    val cancelDownload: ModelType -> Result<unit, WhisperError>

    /// Get download progress for specific model
    val getDownloadProgress: ModelType -> float option