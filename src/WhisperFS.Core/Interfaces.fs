namespace WhisperFS

open System
open System.IO

/// Unified client interface (combines batch and streaming)
type IWhisperClient =
    inherit IDisposable

    /// Process audio based on mode (batch or streaming)
    abstract member ProcessAsync: samples:float32[] -> Async<Result<TranscriptionResult, WhisperError>>

    /// Process audio stream (for streaming mode)
    abstract member ProcessStream: audioStream:IObservable<float32[]> -> IObservable<TranscriptionEvent>

    /// Process audio file
    abstract member ProcessFileAsync: path:string -> Async<Result<TranscriptionResult, WhisperError>>

    /// Get/set streaming mode
    abstract member StreamingMode: bool with get, set

    /// Observable events (for streaming)
    abstract member Events: IObservable<TranscriptionEvent>

    /// Reset state (for streaming)
    abstract member Reset: unit -> unit

    /// Detect language
    abstract member DetectLanguageAsync: samples:float32[] -> Async<Result<LanguageDetection, WhisperError>>

/// Legacy batch processor interface (for backward compatibility)
[<Obsolete("Use IWhisperClient for new implementations")>]
type IWhisperProcessor =
    inherit IDisposable
    abstract member ProcessAsync: audioPath:string -> Async<TranscriptionResult>
    abstract member ProcessAsync: audioStream:Stream -> IAsyncEnumerable<Segment>
    abstract member ProcessAsync: samples:float32[] -> Async<TranscriptionResult>

/// Model factory (enhanced from WhisperFactory)
type IWhisperFactory =
    inherit IDisposable
    abstract member CreateClient: config:WhisperConfig -> IWhisperClient
    abstract member FromPath: modelPath:string -> Result<IWhisperFactory, WhisperError>
    abstract member FromBuffer: buffer:byte[] -> Result<IWhisperFactory, WhisperError>
    abstract member GetModelInfo: unit -> ModelInfo

/// Model management
module ModelManagement =

    /// Download model (equivalent to WhisperGgmlDownloader)
    type IModelDownloader =
        abstract member DownloadModelAsync: modelType:ModelType -> Async<Result<string, WhisperError>>
        abstract member GetModelPath: modelType:ModelType -> string
        abstract member IsModelDownloaded: modelType:ModelType -> bool
        abstract member GetDownloadProgress: unit -> float

/// Audio capture interface
type IAudioCapture =
    inherit IDisposable
    abstract member StartCapture: unit -> unit
    abstract member StopCapture: unit -> unit
    abstract member AudioFrameAvailable: IObservable<float32[]>
    abstract member SampleRate: int
    abstract member Channels: int

/// Voice Activity Detection interface
type IVoiceActivityDetector =
    abstract member ProcessFrame: samples:float32[] -> VadResult
    abstract member Reset: unit -> unit
    abstract member Sensitivity: float32 with get, set

and VadResult =
    | SpeechStarted
    | SpeechContinuing
    | SpeechEnded of duration:TimeSpan
    | Silence

/// Transcription service interface
type ITranscriptionService =
    inherit IDisposable
    abstract member StartRecording: unit -> unit
    abstract member StopRecording: unit -> unit
    abstract member GetStatus: unit -> ServiceStatus
    abstract member OnTranscription: IObservable<TranscriptionEvent>
    abstract member OnStatusChanged: IObservable<ServiceStatus>

and ServiceStatus =
    | Idle
    | Recording
    | Transcribing
    | Error of string