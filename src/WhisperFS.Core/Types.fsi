namespace WhisperFS

open System

/// Model types with size information for download progress and storage management
type ModelType =
    | Tiny | TinyEn
    | Base | BaseEn
    | Small | SmallEn
    | Medium | MediumEn
    | LargeV1 | LargeV2 | LargeV3
    | Custom of path:string

    member GetModelSize: unit -> int64
    member GetModelName: unit -> string

/// Token with confidence scoring for quality assessment
type Token = {
    Text: string
    Timestamp: float32
    Probability: float32
    IsSpecial: bool
}

/// Segment with precise timing for A/V sync and editing
type Segment = {
    Text: string
    StartTime: float32
    EndTime: float32
    Tokens: Token list
}

/// Complete transcription with metadata for application integration
type TranscriptionResult = {
    FullText: string
    Segments: Segment list
    Duration: TimeSpan
    ProcessingTime: TimeSpan
    Timestamp: DateTime
    Language: string option
    LanguageConfidence: float32 option
    Tokens: Token list option
}

/// Language detection with confidence scoring
type LanguageDetection = {
    Language: string
    Confidence: float32
    Probabilities: Map<string, float32>
}

/// Performance metrics for monitoring transcription efficiency
type PerformanceMetrics = {
    TotalProcessingTime: TimeSpan
    TotalAudioProcessed: TimeSpan
    AverageRealTimeFactor: float
    SegmentsProcessed: int
    TokensGenerated: int
    ErrorCount: int
}

/// Streaming events for real-time transcription
type TranscriptionEvent =
    | PartialTranscription of text:string * tokens:Token list * confidence:float32
    | FinalTranscription of text:string * tokens:Token list * segments:Segment list
    | ContextUpdate of contextData:byte[]
    | ProcessingError of error:string

/// Request queuing for batch processing
type TranscriptionRequest = {
    Audio: float32[]
    SampleRate: int
    Timestamp: DateTime
    RequestId: Guid
}

/// Model capabilities and parameters
type ModelInfo = {
    Type: ModelType
    VocabSize: int
    AudioContext: int
    AudioState: int
    Languages: string list
}

/// Sampling strategies for transcription quality vs speed
type SamplingStrategy =
    | Greedy
    | BeamSearch of beamSize:int * bestOf:int

/// Comprehensive whisper.cpp configuration for fine-tuning
type WhisperConfig = {
    // Core model settings
    ModelPath: string
    ModelType: ModelType
    Language: string option

    // Streaming configuration
    ChunkSizeMs: int

    // Performance and threading
    Strategy: SamplingStrategy
    ThreadCount: int
    MaxTextContext: int

    // Audio processing parameters
    OffsetMs: int
    DurationMs: int
    AudioContext: int

    // Translation and language handling
    Translate: bool
    DetectLanguage: bool
    InitialPrompt: string option

    // Output formatting
    NoContext: bool
    NoTimestamps: bool
    SingleSegment: bool
    PrintSpecial: bool
    PrintProgress: bool
    PrintRealtime: bool
    PrintTimestamps: bool

    // Token-level processing
    TokenTimestamps: bool
    ThresholdPt: float32
    ThresholdPtSum: float32
    MaxLen: int
    SplitOnWord: bool
    MaxTokens: int

    // Quality and filtering
    Temperature: float32
    MaxInitialTs: float32
    LengthPenalty: float32
    TemperatureInc: float32
    EntropyThreshold: float32
    LogProbThreshold: float32
    NoSpeechThreshold: float32
    SuppressBlank: bool
    SuppressNonSpeech: bool

    // Debugging
    DebugMode: bool
}

module WhisperConfig =
    /// Default configuration optimized for general use
    val defaultConfig: WhisperConfig