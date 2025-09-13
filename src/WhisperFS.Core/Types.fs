namespace WhisperFS

open System

/// Model types matching Whisper.NET's GgmlType
type ModelType =
    | Tiny | TinyEn
    | Base | BaseEn
    | Small | SmallEn
    | Medium | MediumEn
    | LargeV1 | LargeV2 | LargeV3
    | Custom of path:string

    member this.GetModelSize() =
        match this with
        | Tiny | TinyEn -> 39L * 1024L * 1024L
        | Base | BaseEn -> 142L * 1024L * 1024L
        | Small | SmallEn -> 466L * 1024L * 1024L
        | Medium | MediumEn -> 1500L * 1024L * 1024L
        | LargeV1 | LargeV2 | LargeV3 -> 3000L * 1024L * 1024L
        | Custom _ -> 0L

    member this.GetModelName() =
        match this with
        | Tiny -> "ggml-tiny"
        | TinyEn -> "ggml-tiny.en"
        | Base -> "ggml-base"
        | BaseEn -> "ggml-base.en"
        | Small -> "ggml-small"
        | SmallEn -> "ggml-small.en"
        | Medium -> "ggml-medium"
        | MediumEn -> "ggml-medium.en"
        | LargeV1 -> "ggml-large-v1"
        | LargeV2 -> "ggml-large-v2"
        | LargeV3 -> "ggml-large-v3"
        | Custom path -> path

/// Token information with confidence
type Token = {
    Text: string
    Timestamp: float32
    Probability: float32
    IsSpecial: bool
}

/// Segment with timing and tokens
type Segment = {
    Text: string
    StartTime: float32
    EndTime: float32
    Tokens: Token list
}

/// Transcription result
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

/// Language detection result
type LanguageDetection = {
    Language: string
    Confidence: float32
    Probabilities: Map<string, float32>
}

/// Performance metrics for monitoring
type PerformanceMetrics = {
    TotalProcessingTime: TimeSpan
    TotalAudioProcessed: TimeSpan
    AverageRealTimeFactor: float
    SegmentsProcessed: int
    TokensGenerated: int
    ErrorCount: int
}

/// Streaming transcription events
type TranscriptionEvent =
    | PartialTranscription of text:string * tokens:Token list * confidence:float32
    | FinalTranscription of text:string * tokens:Token list * segments:Segment list
    | ContextUpdate of contextData:byte[]
    | ProcessingError of error:string

/// Transcription request for queuing
type TranscriptionRequest = {
    Audio: float32[]
    SampleRate: int
    Timestamp: DateTime
    RequestId: Guid
}

/// Model information
type ModelInfo = {
    Type: ModelType
    VocabSize: int
    AudioContext: int
    AudioState: int
    Languages: string list
}

/// Sampling strategy
type SamplingStrategy =
    | Greedy
    | BeamSearch of beamSize:int * bestOf:int

/// Complete configuration
type WhisperConfig = {
    // Core options
    ModelPath: string
    ModelType: ModelType
    Language: string option  // None for auto-detect
    ThreadCount: int
    UseGpu: bool
    EnableTranslate: bool
    MaxSegmentLength: int

    // Advanced options from whisper.cpp
    Temperature: float32
    TemperatureInc: float32
    EntropyThreshold: float32
    LogProbThreshold: float32
    NoSpeechThreshold: float32
    SamplingStrategy: SamplingStrategy
    MaxTokensPerSegment: int
    AudioContext: int
    NoContext: bool
    SingleSegment: bool
    PrintSpecialTokens: bool
    PrintProgress: bool
    PrintTimestamps: bool
    TokenTimestamps: bool
    ThresholdPt: float32
    ThresholdPtSum: float32
    MaxLen: int
    SplitOnWord: bool
    MaxTokens: int
    SpeedUp: bool
    DebugMode: bool
    InitialPrompt: string option
    SuppressBlank: bool
    SuppressNonSpeechTokens: bool
    MaxInitialTs: float32
    LengthPenalty: float32

    // Streaming-specific options
    StreamingMode: bool
    ChunkSizeMs: int
    OverlapMs: int
    MinConfidence: float32
    MaxContext: int
    StabilityThreshold: float32
}

module WhisperConfig =
    /// Default configuration
    let defaultConfig = {
        ModelPath = ""
        ModelType = Base
        Language = Some "en"
        ThreadCount = Environment.ProcessorCount
        UseGpu = false
        EnableTranslate = false
        MaxSegmentLength = 0
        Temperature = 0.0f
        TemperatureInc = 0.2f
        EntropyThreshold = 2.4f
        LogProbThreshold = -1.0f
        NoSpeechThreshold = 0.6f
        SamplingStrategy = Greedy
        MaxTokensPerSegment = 0
        AudioContext = 0
        NoContext = false
        SingleSegment = false
        PrintSpecialTokens = false
        PrintProgress = false
        PrintTimestamps = false
        TokenTimestamps = false
        ThresholdPt = 0.01f
        ThresholdPtSum = 0.01f
        MaxLen = 0
        SplitOnWord = false
        MaxTokens = 0
        SpeedUp = false
        DebugMode = false
        InitialPrompt = None
        SuppressBlank = true
        SuppressNonSpeechTokens = true
        MaxInitialTs = 1.0f
        LengthPenalty = -1.0f
        StreamingMode = false
        ChunkSizeMs = 1000
        OverlapMs = 200
        MinConfidence = 0.5f
        MaxContext = 512
        StabilityThreshold = 0.7f
    }