namespace WhisperFS.Runtime

open System
open WhisperFS

/// Model download and management status tracking
type ModelStatus =
    | NotDownloaded
    | Downloading of progress:float
    | Downloaded of path:string * size:int64
    | Failed of error:string

/// Complete model metadata with download information
type ModelMetadata = {
    Type: ModelType
    DisplayName: string
    Description: string
    Size: int64
    Url: string
    Sha256: string option
    RequiresGpu: bool
    Languages: string list
}

/// Download progress tracking with performance metrics
type DownloadProgress = {
    ModelType: ModelType
    BytesDownloaded: int64
    TotalBytes: int64
    PercentComplete: float
    BytesPerSecond: float
    EstimatedTimeRemaining: TimeSpan option
}

/// Model management event stream for UI integration
type ModelEvent =
    | DownloadStarted of ModelType
    | DownloadProgress of DownloadProgress
    | DownloadCompleted of ModelType * path:string
    | DownloadFailed of ModelType * error:string
    | ModelDeleted of ModelType
    | VerificationStarted of ModelType
    | VerificationCompleted of ModelType * success:bool

/// Model repository configuration
type ModelRepository = {
    Name: string
    BaseUrl: string
    Models: ModelMetadata list
}