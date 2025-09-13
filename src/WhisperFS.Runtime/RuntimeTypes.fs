namespace WhisperFS.Runtime

open System
open WhisperFS

/// Model download status
type ModelStatus =
    | NotDownloaded
    | Downloading of progress:float  // 0.0 to 1.0
    | Downloaded of path:string * size:int64
    | Failed of error:string

/// Model metadata
type ModelMetadata = {
    Type: ModelType
    DisplayName: string
    Description: string
    Size: int64  // Size in bytes
    Url: string
    Sha256: string option  // For verification
    RequiresGpu: bool
    Languages: string list  // Supported languages
}

/// Download progress event
type DownloadProgress = {
    ModelType: ModelType
    BytesDownloaded: int64
    TotalBytes: int64
    PercentComplete: float
    BytesPerSecond: float
    EstimatedTimeRemaining: TimeSpan option
}

/// Model management events
type ModelEvent =
    | DownloadStarted of ModelType
    | DownloadProgress of DownloadProgress
    | DownloadCompleted of ModelType * path:string
    | DownloadFailed of ModelType * error:string
    | ModelDeleted of ModelType
    | VerificationStarted of ModelType
    | VerificationCompleted of ModelType * success:bool

/// Model repository information
type ModelRepository = {
    Name: string
    BaseUrl: string
    Models: ModelMetadata list
}