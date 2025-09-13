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

    // Streaming options
    ChunkSizeMs: int           // Chunk size for streaming audio

    // whisper.cpp parameters (matching whisper_full_params)
    Strategy: SamplingStrategy  // sampling strategy
    ThreadCount: int            // n_threads
    MaxTextContext: int         // n_max_text_ctx
    OffsetMs: int              // offset_ms
    DurationMs: int            // duration_ms
    Translate: bool            // translate
    NoContext: bool            // no_context
    NoTimestamps: bool         // no_timestamps
    SingleSegment: bool        // single_segment
    PrintSpecial: bool         // print_special
    PrintProgress: bool        // print_progress
    PrintRealtime: bool        // print_realtime
    PrintTimestamps: bool      // print_timestamps

    // Token-level timestamps
    TokenTimestamps: bool      // token_timestamps
    ThresholdPt: float32       // thold_pt
    ThresholdPtSum: float32    // thold_ptsum
    MaxLen: int                // max_len
    SplitOnWord: bool          // split_on_word
    MaxTokens: int             // max_tokens

    // Audio and debugging
    DebugMode: bool            // debug_mode
    AudioContext: int          // audio_ctx

    // Speaker diarization
    EnableDiarization: bool    // tdrz_enable

    // Language and prompting
    InitialPrompt: string option  // initial_prompt
    DetectLanguage: bool          // detect_language

    // Token suppression
    SuppressBlank: bool           // suppress_blank
    SuppressNonSpeech: bool       // suppress_nst
    SuppressRegex: string option  // suppress_regex

    // Voice Activity Detection
    EnableVAD: bool               // vad
    VADModelPath: string option   // vad_model_path

    // Grammar constraints
    GrammarRules: string option   // Will be converted to grammar rules

    // Temperature and penalties
    Temperature: float32          // temperature
    MaxInitialTs: float32         // max_initial_ts
    LengthPenalty: float32        // length_penalty
    TemperatureInc: float32       // temperature_inc
    EntropyThreshold: float32     // entropy_thold
    LogProbThreshold: float32     // logprob_thold
    NoSpeechThreshold: float32    // no_speech_thold
}

module WhisperConfig =
    /// Default configuration
    let defaultConfig = {
        ModelPath = ""
        ModelType = Base
        Language = None
        ChunkSizeMs = 1000  // 1 second chunks for streaming
        Strategy = Greedy
        ThreadCount = Environment.ProcessorCount
        MaxTextContext = 16384
        OffsetMs = 0
        DurationMs = 0
        Translate = false
        NoContext = false
        NoTimestamps = false
        SingleSegment = false
        PrintSpecial = false
        PrintProgress = false
        PrintRealtime = false
        PrintTimestamps = false
        TokenTimestamps = false
        ThresholdPt = 0.01f
        ThresholdPtSum = 0.01f
        MaxLen = 0
        SplitOnWord = false
        MaxTokens = 0
        DebugMode = false
        AudioContext = 0
        EnableDiarization = false
        InitialPrompt = None
        DetectLanguage = false
        SuppressBlank = true
        SuppressNonSpeech = true
        SuppressRegex = None
        EnableVAD = false
        VADModelPath = None
        GrammarRules = None
        Temperature = 0.0f
        MaxInitialTs = 1.0f
        LengthPenalty = -1.0f
        TemperatureInc = 0.2f
        EntropyThreshold = 2.4f
        LogProbThreshold = -1.0f
        NoSpeechThreshold = 0.6f
    }