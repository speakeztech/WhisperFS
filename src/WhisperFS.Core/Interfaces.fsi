namespace WhisperFS

open System
open System.IO
open System.Collections.Generic

/// Input types for unified audio processing
type WhisperInput =
    | BatchAudio of samples:float32[]
    | StreamingAudio of stream:IObservable<float32[]>
    | AudioFile of path:string

/// Output types for unified processing results
type WhisperOutput =
    | BatchResult of Async<Result<TranscriptionResult, WhisperError>>
    | StreamingResult of IObservable<Result<TranscriptionEvent, WhisperError>>

/// Primary client interface with comprehensive transcription capabilities
type IWhisperClient =
    inherit IDisposable

    /// Process audio samples in batch mode
    abstract member ProcessAsync: samples:float32[] -> Async<Result<TranscriptionResult, WhisperError>>

    /// Process streaming audio with real-time events
    abstract member ProcessStream: audioStream:IObservable<float32[]> -> IObservable<Result<TranscriptionEvent, WhisperError>>

    /// Process audio file directly
    abstract member ProcessFileAsync: path:string -> Async<Result<TranscriptionResult, WhisperError>>

    /// Unified processing based on input type
    abstract member Process: input:WhisperInput -> WhisperOutput

    /// All transcription events stream
    abstract member Events: IObservable<Result<TranscriptionEvent, WhisperError>>

    /// Reset state for streaming continuation
    abstract member Reset: unit -> Result<unit, WhisperError>

    /// Detect language from audio samples
    abstract member DetectLanguageAsync: samples:float32[] -> Async<Result<LanguageDetection, WhisperError>>

    /// Get performance metrics for monitoring
    abstract member GetMetrics: unit -> PerformanceMetrics

/// Legacy batch processor (for backward compatibility)
[<System.Obsolete("Use IWhisperClient for new implementations")>]
type IWhisperProcessor =
    inherit IDisposable
    abstract member ProcessAsync: audioPath:string -> Async<TranscriptionResult>
    abstract member ProcessAsync: audioStream:Stream -> IAsyncEnumerable<Segment>
    abstract member ProcessAsync: samples:float32[] -> Async<TranscriptionResult>

/// Model factory for client creation
type IWhisperFactory =
    inherit IDisposable
    abstract member CreateClient: config:WhisperConfig -> Result<IWhisperClient, WhisperError>
    abstract member FromPath: modelPath:string -> Result<IWhisperFactory, WhisperError>
    abstract member FromBuffer: buffer:byte[] -> Result<IWhisperFactory, WhisperError>
    abstract member GetModelInfo: unit -> ModelType

/// Model management interfaces
module ModelManagement =

    /// Model downloading with progress tracking
    type IModelDownloader =
        abstract member DownloadModelAsync: modelType:ModelType -> Async<Result<string, WhisperError>>
        abstract member GetModelPath: modelType:ModelType -> string
        abstract member IsModelDownloaded: modelType:ModelType -> bool
        abstract member GetDownloadProgress: unit -> float

/// Audio capture interface for real-time processing
type IAudioCapture =
    inherit IDisposable
    abstract member StartCapture: unit -> Result<unit, WhisperError>
    abstract member StopCapture: unit -> Result<unit, WhisperError>
    abstract member AudioFrameAvailable: IObservable<float32[]>
    abstract member SampleRate: int
    abstract member Channels: int

/// Voice Activity Detection for speech boundary detection
type IVoiceActivityDetector =
    abstract member ProcessFrame: samples:float32[] -> VadResult
    abstract member Reset: unit -> unit
    abstract member Sensitivity: float32 with get, set

and VadResult =
    | SpeechStarted
    | SpeechContinuing
    | SpeechEnded of duration:TimeSpan
    | Silence

/// High-level transcription service with state management
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
    | ServiceError of string