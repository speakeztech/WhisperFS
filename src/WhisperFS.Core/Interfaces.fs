namespace WhisperFS

open System
open System.IO
open System.Collections.Generic

/// Input types for unified processing
type WhisperInput =
    | BatchAudio of samples:float32[]
    | StreamingAudio of stream:IObservable<float32[]>
    | AudioFile of path:string

/// Output types for unified processing
type WhisperOutput =
    | BatchResult of Async<Result<TranscriptionResult, WhisperError>>
    | StreamingResult of IObservable<Result<TranscriptionEvent, WhisperError>>

/// Unified client interface with consistent Result types
type IWhisperClient =
    inherit IDisposable

    /// Process audio samples (batch mode)
    abstract member ProcessAsync: samples:float32[] -> Async<Result<TranscriptionResult, WhisperError>>

    /// Process audio stream (streaming mode) - returns observable of results
    abstract member ProcessStream: audioStream:IObservable<float32[]> -> IObservable<Result<TranscriptionEvent, WhisperError>>

    /// Process audio file
    abstract member ProcessFileAsync: path:string -> Async<Result<TranscriptionResult, WhisperError>>

    /// Process with either batch or streaming based on input type
    abstract member Process: input:WhisperInput -> WhisperOutput

    /// Observable events for all transcription updates
    abstract member Events: IObservable<Result<TranscriptionEvent, WhisperError>>

    /// Reset state (for streaming)
    abstract member Reset: unit -> Result<unit, WhisperError>

    /// Detect language from audio samples
    abstract member DetectLanguageAsync: samples:float32[] -> Async<Result<LanguageDetection, WhisperError>>

    /// Get current performance metrics
    abstract member GetMetrics: unit -> PerformanceMetrics

/// Legacy batch processor interface (for backward compatibility)
[<Obsolete("Use IWhisperClient for new implementations")>]
type IWhisperProcessor =
    inherit IDisposable
    abstract member ProcessAsync: audioPath:string -> Async<TranscriptionResult>
    abstract member ProcessAsync: audioStream:Stream -> IAsyncEnumerable<Segment>
    abstract member ProcessAsync: samples:float32[] -> Async<TranscriptionResult>

/// Model factory with Result types
type IWhisperFactory =
    inherit IDisposable
    abstract member CreateClient: config:WhisperConfig -> Result<IWhisperClient, WhisperError>
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
    abstract member StartCapture: unit -> Result<unit, WhisperError>
    abstract member StopCapture: unit -> Result<unit, WhisperError>
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

/// Transcription service interface with Result types
type ITranscriptionService =
    inherit IDisposable
    abstract member StartRecording: unit -> Result<unit, WhisperError>
    abstract member StopRecording: unit -> Result<unit, WhisperError>
    abstract member GetStatus: unit -> ServiceStatus
    abstract member OnTranscription: IObservable<Result<TranscriptionEvent, WhisperError>>
    abstract member OnStatusChanged: IObservable<ServiceStatus>

and ServiceStatus =
    | Idle
    | Recording
    | Transcribing
    | Error of string