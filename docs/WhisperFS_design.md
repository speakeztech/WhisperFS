# WhisperFS: A Streaming-First F# Wrapper for Whisper.cpp

## Executive Summary

WhisperFS is a proposed F# library that provides comprehensive bindings to whisper.cpp, offering both full feature parity with Whisper.NET for push-to-talk (PTT) scenarios and advanced streaming capabilities for real-time transcription. This "one-stop shopping" library consolidates all transcription needs, eliminating the need for multiple dependencies. This design document captures lessons learned from implementing both Whisper.NET (batch processing) and Vosk (unstable streaming) integrations, and proposes a unified solution.

## Motivation and Lessons Learned

### The Whisper.NET Experience

Our initial implementation used Whisper.NET, which provided excellent accuracy but suffered from fundamental limitations:

1. **Batch-Only Processing**: Whisper.NET only exposes `ProcessAsync`, requiring complete audio segments
2. **No Streaming Support**: Users had to wait until button release to see any text
3. **Lack of Intermediate Results**: No access to partial transcriptions or token-level callbacks
4. **Memory Inefficiency**: Had to buffer entire audio segments in memory

Example of the limitation we faced:
```fsharp
// Current Whisper.NET approach - all or nothing
let processWhisper (audioBuffer: float32[]) =
    async {
        // Must wait for complete audio
        let! result = processor.ProcessAsync(audioBuffer)
        return result.Text  // Only get final result
    }
```

### The Vosk Experiment

We then tried Vosk for true streaming, which revealed different problems:

1. **Unstable Partial Results**: Text would completely change mid-stream ("heard" → "hood")
2. **Aggressive Corrections**: Massive backspacing and rewriting that confused users
3. **Poor Grammar**: Lack of punctuation and capitalization
4. **Inconsistent Confidence**: No reliable way to know when text was stable

Example of Vosk's instability:
```
Stream: "this is a test to see if the" 
Stream: "cursor"  // Complete change!
Stream: "jump is still"
Stream: "present"
Final: "this is a test to see if the cursor jump is still present"
```

### Key Insights

1. **Streaming !== Real-time Token Output**: True streaming ASR (like Android voice typing) uses models specifically designed for stable incremental output
2. **Whisper's Strength**: Whisper excels at accuracy because it uses full context - this is also why it's not naturally streaming
3. **The Chunking Compromise**: Processing overlapping chunks with Whisper can provide periodic updates while maintaining accuracy
4. **Context is Critical**: Maintaining context between chunks is essential for coherent transcription
5. **Feature Parity Essential**: Must support all existing Whisper.NET features while adding streaming capabilities

## WhisperFS Design Philosophy

### Core Principles

1. **Complete Feature Parity**: Support ALL Whisper.NET features for seamless migration
2. **Streaming-First Architecture**: Every API designed with streaming as the primary use case
3. **F# Idiomatic**: Leverage F#'s strengths - discriminated unions, async workflows, observables
4. **Zero-Copy Where Possible**: Direct memory management for audio buffers
5. **Flexible Confidence Models**: Let applications decide when text is "stable enough" to display
6. **Unified API**: Single library for both PTT and streaming scenarios

### Architecture Overview

```fsharp
namespace WhisperFS

open System
open System.IO

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

/// Complete configuration matching and extending Whisper.NET
type WhisperConfig = {
    // Core Whisper.NET compatible options
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
    BeamSize: int
    BestOf: int
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
    AudioCtx: int
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

/// Model management matching Whisper.NET
module ModelManagement =

    /// Download model (equivalent to WhisperGgmlDownloader)
    type IModelDownloader =
        abstract member DownloadModelAsync: modelType:ModelType -> Async<string>
        abstract member GetModelPath: modelType:ModelType -> string
        abstract member IsModelDownloaded: modelType:ModelType -> bool
        abstract member GetDownloadProgress: unit -> float

    /// Model factory (enhanced from WhisperFactory)
    type IWhisperFactory =
        inherit IDisposable
        abstract member CreateClient: config:WhisperConfig -> IWhisperClient
        abstract member FromPath: modelPath:string -> Result<IWhisperFactory, WhisperError>
        abstract member FromBuffer: buffer:byte[] -> Result<IWhisperFactory, WhisperError>
        abstract member GetModelInfo: unit -> ModelInfo

    and ModelInfo = {
        Type: ModelType
        VocabSize: int
        AudioContext: int
        AudioState: int
        Languages: string list
    }

namespace WhisperFS

open System
open System.Runtime.InteropServices

/// Core streaming types
type TranscriptionEvent =
    | PartialTranscription of text:string * tokens:Token list * confidence:float32
    | FinalTranscription of text:string * tokens:Token list * segments:Segment list
    | ContextUpdate of contextData:byte[]
    | ProcessingError of error:string

and Token = {
    Text: string
    Timestamp: float32
    Probability: float32
    IsSpecial: bool
}

and Segment = {
    Text: string
    StartTime: float32
    EndTime: float32
    Tokens: Token list
}

/// Streaming configuration
type StreamConfig = {
    /// Size of audio chunks in milliseconds
    ChunkSizeMs: int
    /// Overlap between chunks in milliseconds  
    OverlapMs: int
    /// Minimum confidence to emit partial results
    MinConfidence: float32
    /// Maximum context to maintain (in tokens)
    MaxContext: int
    /// Enable token-level timestamps
    TokenTimestamps: bool
    /// Language hint (empty for auto-detect)
    Language: string
    /// Prompt to guide transcription style
    InitialPrompt: string
}

/// Legacy batch processor interface (for backward compatibility)
/// Note: New code should use IWhisperClient instead
[<Obsolete("Use IWhisperClient for new implementations")>]
type IWhisperProcessor =
    inherit IDisposable
    abstract member ProcessAsync: audioPath:string -> Async<TranscriptionResult>
    abstract member ProcessAsync: audioStream:Stream -> IAsyncEnumerable<Segment>
    abstract member ProcessAsync: samples:float32[] -> Async<TranscriptionResult>

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

/// Error types for better error handling
and WhisperError =
    | ModelLoadError of message:string
    | ProcessingError of code:int * message:string
    | InvalidAudioFormat of message:string
    | StateError of message:string
    | NativeLibraryError of message:string
    | TokenizationError of message:string
    | OutOfMemory
    | Cancelled

and LanguageDetection = {
    Language: string
    Confidence: float32
    Probabilities: Map<string, float32>
}

/// Unified result type (Whisper.NET compatible)
and TranscriptionResult = {
    FullText: string
    Segments: Segment list
    Duration: TimeSpan
    ProcessingTime: TimeSpan
    Timestamp: DateTime
    Language: string option
    LanguageConfidence: float32 option
    Tokens: Token list option  // Extended from Whisper.NET
}
```

## Native Library Management

```fsharp
/// Runtime selection and native library loading
module NativeLibraryLoader =

    type RuntimeType =
        | Cpu
        | CpuNoAvx
        | Cuda of version:string
        | CoreML
        | OpenVino
        | Vulkan

    type RuntimeInfo = {
        Type: RuntimeType
        Priority: int
        LibraryName: string
        Available: bool
    }

    /// Detect available runtimes based on system capabilities
    let detectAvailableRuntimes() =
        [
            // Check CUDA availability
            if hasCudaSupport() then
                { Type = Cuda "12.0"; Priority = 1; LibraryName = "whisper.cuda.dll"; Available = true }

            // Check AVX support
            if hasAvxSupport() then
                { Type = Cpu; Priority = 2; LibraryName = "whisper.dll"; Available = true }
            else
                { Type = CpuNoAvx; Priority = 3; LibraryName = "whisper.noavx.dll"; Available = true }

            // Platform-specific
            if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
                { Type = CoreML; Priority = 1; LibraryName = "whisper.coreml.dylib"; Available = true }
        ]
        |> List.sortBy (fun r -> r.Priority)

    /// Load the best available runtime
    let loadBestRuntime() =
        let runtimes = detectAvailableRuntimes()
        match runtimes with
        | [] -> Error (NativeLibraryError "No compatible runtime found")
        | best::_ ->
            try
                NativeLibrary.Load(best.LibraryName)
                Ok best
            with ex ->
                Error (NativeLibraryError ex.Message)

    /// Download runtime if not present
    let ensureRuntimeAsync(runtime: RuntimeType) =
        async {
            let libraryPath = getLibraryPath runtime
            if not (File.Exists(libraryPath)) then
                let url = getRuntimeUrl runtime
                let! data = downloadAsync url
                do! File.WriteAllBytesAsync(libraryPath, data) |> Async.AwaitTask
            return Ok libraryPath
        }
```

## Implementation Details

### Fluent Builder Pattern (Enhanced from Whisper.NET)

```fsharp
/// Enhanced builder pattern with Result type
type WhisperBuilder(factory: IWhisperFactory) =
    let mutable config = {
        ModelPath = ""
        ModelType = Base
        Language = None
        ThreadCount = Environment.ProcessorCount
        UseGpu = false
        EnableTranslate = false
        MaxSegmentLength = 0
        Temperature = 0.0f
        TemperatureInc = 0.2f
        BeamSize = 5
        BestOf = 5
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
        AudioCtx = 0
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

    member _.WithLanguage(lang: string) =
        config <- { config with Language = Some lang }
        this

    member _.WithLanguageDetection() =
        config <- { config with Language = None }
        this

    member _.WithThreads(threads: int) =
        config <- { config with ThreadCount = threads }
        this

    member _.WithGpu() =
        config <- { config with UseGpu = true }
        this

    member _.WithTranslate() =
        config <- { config with EnableTranslate = true }
        this

    member _.WithMaxSegmentLength(length: int) =
        config <- { config with MaxSegmentLength = length }
        this

    member _.WithPrompt(prompt: string) =
        config <- { config with InitialPrompt = Some prompt }
        this

    member _.WithTokenTimestamps() =
        config <- { config with TokenTimestamps = true }
        this

    member _.WithBeamSearch(size: int) =
        config <- { config with BeamSize = size }
        this

    member _.WithTemperature(temp: float32) =
        config <- { config with Temperature = temp }
        this

    member _.WithStreaming(chunkMs: int, overlapMs: int) =
        config <- { config with
                        StreamingMode = true
                        ChunkSizeMs = chunkMs
                        OverlapMs = overlapMs }
        this

    member _.Build() : Result<IWhisperClient, WhisperError> =
        try
            // Validate configuration
            match validateConfig config with
            | Error err -> Error err
            | Ok _ ->
                // Load native library if needed
                match NativeLibraryLoader.loadBestRuntime() with
                | Error err -> Error err
                | Ok runtime ->
                    // Create client
                    let client = factory.CreateClient(config)
                    Ok client
        with ex ->
            Error (NativeLibraryError ex.Message)

    member _.BuildStream() : Result<IWhisperClient, WhisperError> =
        this.WithStreaming(1000, 200).Build()
```

### P/Invoke Bindings to whisper.cpp

```fsharp
module WhisperNative =

    // Context initialization
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_init_from_file(string path)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_init_from_buffer(IntPtr buffer, int buffer_size)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern void whisper_free(IntPtr ctx)

    // State management - CRITICAL for streaming
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_init_state(IntPtr ctx)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern void whisper_free_state(IntPtr state)

    // Core processing functions
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full(
        IntPtr ctx,
        WhisperFullParams parameters,
        float32[] samples,
        int n_samples)

    // Streaming-specific processing with state
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_with_state(
        IntPtr ctx,
        IntPtr state,
        WhisperFullParams parameters,
        float32[] samples,
        int n_samples)

    // Segment access from context
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_n_segments(IntPtr ctx)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_full_get_segment_text(IntPtr ctx, int i_segment)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t0(IntPtr ctx, int i_segment)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t1(IntPtr ctx, int i_segment)

    // Segment access from state (for streaming)
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_n_segments_from_state(IntPtr state)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_full_get_segment_text_from_state(IntPtr state, int i_segment)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t0_from_state(IntPtr state, int i_segment)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t1_from_state(IntPtr state, int i_segment)

    // Token access
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_n_tokens(IntPtr ctx, int i_segment)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_n_tokens_from_state(IntPtr state, int i_segment)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_full_get_token_text(IntPtr ctx, int i_segment, int i_token)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_full_get_token_text_from_state(IntPtr state, int i_segment, int i_token)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern float whisper_full_get_token_p(IntPtr ctx, int i_segment, int i_token)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern float whisper_full_get_token_p_from_state(IntPtr state, int i_segment, int i_token)

    // Tokenization for prompts
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_tokenize(
        IntPtr ctx,
        [<MarshalAs(UnmanagedType.LPStr)>] string text,
        [<Out>] int[] tokens,
        int n_max_tokens)

    // Language detection
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_lang_auto_detect(
        IntPtr ctx,
        int offset_ms,
        int n_threads,
        float[] lang_probs)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_lang_str(int lang_id)

    // Model info
    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_vocab(IntPtr ctx)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_audio_ctx(IntPtr ctx)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_audio_state(IntPtr ctx)

    [<DllImport("whisper.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_type(IntPtr ctx)

    // Callback delegates for streaming
    [<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
    type WhisperNewSegmentCallback =
        delegate of ctx:IntPtr * state:IntPtr * n_new:int * user_data:IntPtr -> unit

    [<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
    type WhisperEncoderBeginCallback =
        delegate of ctx:IntPtr * state:IntPtr * user_data:IntPtr -> bool

    [<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
    type WhisperLogitsFilterCallback =
        delegate of ctx:IntPtr * state:IntPtr * tokens:IntPtr * n_tokens:int * logits:IntPtr * user_data:IntPtr -> unit

    /// Full parameters structure - must match C struct exactly
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type WhisperFullParams =
        val mutable strategy: int                    // Sampling strategy (0=GREEDY, 1=BEAM_SEARCH)
        val mutable n_threads: int                   // Number of threads
        val mutable n_max_text_ctx: int             // Max tokens to use from past text as prompt
        val mutable offset_ms: int                   // Start offset in ms
        val mutable duration_ms: int                 // Audio duration to process in ms
        val mutable translate: bool                  // Translate to English
        val mutable no_context: bool                 // Do not use past transcription for context
        val mutable no_timestamps: bool              // Do not generate timestamps
        val mutable single_segment: bool             // Force single segment output
        val mutable print_special: bool              // Print special tokens
        val mutable print_progress: bool             // Print progress info
        val mutable print_realtime: bool             // Print results from within whisper.cpp
        val mutable print_timestamps: bool           // Print timestamps for each text segment
        val mutable token_timestamps: bool           // Enable token-level timestamps
        val mutable thold_pt: float32               // Timestamp token probability threshold
        val mutable thold_ptsum: float32            // Timestamp token sum probability threshold
        val mutable max_len: int                     // Max segment length in characters
        val mutable split_on_word: bool             // Split on word rather than token
        val mutable max_tokens: int                  // Max tokens per segment (0=no limit)

        // Temperature sampling parameters
        val mutable temperature: float32             // Initial temperature
        val mutable temperature_inc: float32         // Temperature increment for fallbacks
        val mutable entropy_thold: float32          // Entropy threshold for decoder fallback
        val mutable logprob_thold: float32          // Log probability threshold for decoder fallback
        val mutable no_speech_thold: float32        // No-speech probability threshold

        // Beam search parameters (when strategy = BEAM_SEARCH)
        val mutable beam_size: int                   // Beam size for beam search
        val mutable best_of: int                     // Number of best candidates to keep
        val mutable patience: float32                 // Patience for beam search

        // Prompt tokens (must be allocated and tokenized)
        val mutable prompt_tokens: IntPtr            // Pointer to prompt token array
        val mutable prompt_n_tokens: int             // Number of prompt tokens

        // Language
        val mutable language: IntPtr                 // Language hint ("en", "de", etc.)
        val mutable detect_language: bool            // Auto-detect language

        // Suppression
        val mutable suppress_blank: bool             // Suppress blank outputs
        val mutable suppress_non_speech_tokens: bool // Suppress non-speech tokens

        // Initial timestamp
        val mutable max_initial_ts: float32          // Max initial timestamp
        val mutable length_penalty: float32          // Length penalty

        // Callbacks for streaming
        val mutable new_segment_callback: IntPtr     // Callback for new segments
        val mutable new_segment_callback_user_data: IntPtr
        val mutable encoder_begin_callback: IntPtr   // Callback before encoding
        val mutable encoder_begin_callback_user_data: IntPtr
        val mutable logits_filter_callback: IntPtr   // Callback for filtering logits
        val mutable logits_filter_callback_user_data: IntPtr
```

### Unified Client Implementation (Batch and Streaming)

```fsharp
/// Unified implementation supporting both batch and streaming
type WhisperClient(ctx: IntPtr, config: WhisperConfig) =
    let state = if config.StreamingMode then Some(WhisperNative.whisper_init_state(ctx)) else None

    let processBuffer (samples: float32[]) =
        async {
            let startTime = DateTime.UtcNow

            // Setup parameters matching Whisper.NET behavior
            let mutable parameters = WhisperNative.WhisperFullParams()
            parameters.strategy <- 0 // GREEDY
            parameters.n_threads <- config.ThreadCount
            parameters.translate <- config.EnableTranslate
            parameters.no_context <- config.NoContext
            parameters.single_segment <- config.SingleSegment
            parameters.print_special <- config.PrintSpecialTokens
            parameters.print_progress <- config.PrintProgress
            parameters.print_timestamps <- config.PrintTimestamps
            parameters.token_timestamps <- config.TokenTimestamps
            parameters.thold_pt <- config.ThresholdPt
            parameters.thold_ptsum <- config.ThresholdPtSum
            parameters.max_len <- config.MaxLen
            parameters.split_on_word <- config.SplitOnWord
            parameters.max_tokens <- config.MaxTokens
            parameters.suppress_blank <- config.SuppressBlank
            parameters.suppress_non_speech_tokens <- config.SuppressNonSpeechTokens
            parameters.temperature <- config.Temperature
            parameters.max_initial_ts <- config.MaxInitialTs
            parameters.length_penalty <- config.LengthPenalty

            // Set language if specified
            match config.Language with
            | Some lang ->
                parameters.language <- Marshal.StringToHGlobalAnsi(lang)
            | None -> ()

            // Tokenize and set initial prompt if provided
            match config.InitialPrompt with
            | Some prompt ->
                let maxTokens = 512
                let tokens = Array.zeroCreate<int> maxTokens
                let tokenCount = WhisperNative.whisper_tokenize(ctx, prompt, tokens, maxTokens)
                if tokenCount > 0 then
                    // Allocate and copy token IDs (not bytes)
                    let tokenPtr = Marshal.AllocHGlobal(tokenCount * sizeof<int>)
                    Marshal.Copy(tokens, 0, tokenPtr, tokenCount)
                    parameters.prompt_tokens <- tokenPtr
                    parameters.prompt_n_tokens <- tokenCount
            | None -> ()

            // Process audio
            let result = WhisperNative.whisper_full(ctx, parameters, samples, samples.Length)

            if result = 0 then
                // Extract segments and build result
                let segmentCount = WhisperNative.whisper_full_n_segments(ctx)
                let segments = [
                    for i in 0 .. segmentCount - 1 do
                        let textPtr = WhisperNative.whisper_full_get_segment_text(ctx, i)
                        let text = Marshal.PtrToStringAnsi(textPtr)
                        let t0 = WhisperNative.whisper_full_get_segment_t0(ctx, i)
                        let t1 = WhisperNative.whisper_full_get_segment_t1(ctx, i)

                        // Get tokens if requested
                        let tokens =
                            if config.TokenTimestamps then
                                let tokenCount = WhisperNative.whisper_full_n_tokens(ctx, i)
                                [
                                    for j in 0 .. tokenCount - 1 do
                                        let tokenPtr = WhisperNative.whisper_full_get_token_text(ctx, i, j)
                                        let tokenText = Marshal.PtrToStringAnsi(tokenPtr)
                                        let prob = WhisperNative.whisper_full_get_token_p(ctx, i, j)
                                        yield {
                                            Text = tokenText
                                            Timestamp = float32 t0 / 1000.0f  // CORRECT: ms to seconds
                                            Probability = prob
                                            IsSpecial = tokenText.StartsWith("<|") && tokenText.EndsWith("|>")
                                        }
                                ]
                            else
                                []

                        yield {
                            Text = text
                            StartTime = float32 t0 / 1000.0f  // CORRECT: ms to seconds
                            EndTime = float32 t1 / 1000.0f    // CORRECT: ms to seconds
                            Tokens = tokens
                        }
                ]

                let fullText = segments |> List.map (fun s -> s.Text) |> String.concat " "
                let processingTime = DateTime.UtcNow - startTime

                return {
                    FullText = fullText
                    Segments = segments
                    Duration = TimeSpan.FromSeconds(float (samples.Length / 16000))
                    ProcessingTime = processingTime
                    Timestamp = startTime
                    Language = config.Language
                    LanguageConfidence = None
                    Tokens = if config.TokenTimestamps then Some (segments |> List.collect (fun s -> s.Tokens)) else None
                }
            else
                return failwith $"Whisper processing failed with code {result}"
        }

    // Legacy IWhisperProcessor support for backward compatibility
    interface IWhisperProcessor with
        member _.ProcessAsync(audioPath: string) =
            async {
                use stream = File.OpenRead(audioPath)
                let buffer = Array.zeroCreate<byte> (int stream.Length)
                let! _ = stream.AsyncRead(buffer, 0, buffer.Length)
                // Convert to float32 samples (assuming WAV format)
                let samples = convertToFloat32 buffer
                return! processBuffer samples
            }

        member _.ProcessAsync(audioStream: Stream) =
            // Return async enumerable for segment-by-segment processing
            AsyncSeq.unfoldAsync (fun state -> async {
                // Implementation for streaming segments
                return None
            }) ()

        member _.ProcessAsync(samples: float32[]) =
            processBuffer samples

        member _.ProcessAsync(samples: float32[], onSegment: Segment -> unit) =
            async {
                let! result = processBuffer samples
                result.Segments |> List.iter onSegment
                return result
            }

        member _.SetLanguage(language: string) =
            // Update config for next processing
            ()

        member _.DetectLanguageAsync(samples: float32[]) =
            async {
                let langProbs = Array.zeroCreate<float> 100
                let langId = WhisperNative.whisper_lang_auto_detect(ctx, 0, config.ThreadCount, langProbs)
                let langPtr = WhisperNative.whisper_lang_str(langId)
                let lang = Marshal.PtrToStringAnsi(langPtr)
                return (lang, langProbs.[langId])
            }

        member _.Dispose() =
            match state with
            | Some s -> WhisperNative.whisper_free_state(s)
            | None -> ()
            WhisperNative.whisper_free(ctx)

    // Main IWhisperClient implementation
    interface IWhisperClient with
        member this.ProcessAsync(samples) =
            if config.StreamingMode then
                processStreamingChunk samples
            else
                processBatchAudio samples

        member this.ProcessStream(audioStream) =
            if not config.StreamingMode then
                failwith "Client not configured for streaming mode"
            audioStream |> Observable.selectAsync processStreamingChunk

        member this.ProcessFileAsync(path) =
            processFile path

        member _.StreamingMode
            with get() = config.StreamingMode
            and set(value) = () // Would need mutable config

        member _.Events = events.Publish

        member _.Reset() =
            match state with
            | Some s ->
                WhisperNative.whisper_free_state(s)
                // Reinitialize state
            | None -> ()

        member _.DetectLanguageAsync(samples) =
            detectLanguage samples

        member _.Dispose() =
            match state with
            | Some s -> WhisperNative.whisper_free_state(s)
            | None -> ()
            WhisperNative.whisper_free(ctx)
```

### Core Streaming Implementation (Using whisper_state)

```fsharp
type WhisperStream(modelPath: string, config: WhisperConfig) =
    let ctx = WhisperNative.whisper_init_from_file(modelPath)
    let state = WhisperNative.whisper_init_state(ctx)  // CRITICAL: Initialize state for streaming
    let events = Event<TranscriptionEvent>()
    let mutable previousSegmentCount = 0
    let mutable committedText = ""
    let mutable pendingAudio = ResizeArray<float32>()

    // Callback handlers for streaming
    let mutable newSegmentCallback = Unchecked.defaultof<WhisperNative.WhisperNewSegmentCallback>
    let mutable encoderBeginCallback = Unchecked.defaultof<WhisperNative.WhisperEncoderBeginCallback>

    // Setup callbacks
    let setupCallbacks() =
        newSegmentCallback <- WhisperNative.WhisperNewSegmentCallback(fun ctx state n_new userData ->
            // Process new segments as they arrive
            let totalSegments = WhisperNative.whisper_full_n_segments_from_state(state)

            for i in previousSegmentCount .. totalSegments - 1 do
                let textPtr = WhisperNative.whisper_full_get_segment_text_from_state(state, i)
                let text = Marshal.PtrToStringAnsi(textPtr)
                let t0 = WhisperNative.whisper_full_get_segment_t0_from_state(state, i)
                let t1 = WhisperNative.whisper_full_get_segment_t1_from_state(state, i)

                // Get tokens with confidence
                let tokenCount = WhisperNative.whisper_full_n_tokens_from_state(state, i)
                let tokens = [
                    for j in 0 .. tokenCount - 1 do
                        let tokenPtr = WhisperNative.whisper_full_get_token_text_from_state(state, i, j)
                        let tokenText = Marshal.PtrToStringAnsi(tokenPtr)
                        let prob = WhisperNative.whisper_full_get_token_p_from_state(state, i, j)
                        yield {
                            Text = tokenText
                            Timestamp = float32 t0 / 1000.0f  // CORRECT: Convert ms to seconds
                            Probability = prob
                            IsSpecial = tokenText.StartsWith("<|") && tokenText.EndsWith("|>")
                        }
                ]

                let segment = {
                    Text = text
                    StartTime = float32 t0 / 1000.0f  // Convert ms to seconds
                    EndTime = float32 t1 / 1000.0f    // Convert ms to seconds
                    Tokens = tokens
                }

                // Calculate confidence
                let confidence =
                    tokens
                    |> List.filter (fun t -> not t.IsSpecial)
                    |> List.averageBy (fun t -> t.Probability)

                // Emit event
                events.Trigger(PartialTranscription(text, tokens, confidence))

            previousSegmentCount <- totalSegments
        )

        encoderBeginCallback <- WhisperNative.WhisperEncoderBeginCallback(fun ctx state userData ->
            // Return true to proceed with encoding
            true
        )

    do setupCallbacks()

    /// Process chunk incrementally using whisper_state
    let processChunkInternal (samples: float32[]) = async {
        try
            // Append new audio to pending buffer
            pendingAudio.AddRange(samples)

            // Only process if we have enough audio (e.g., 1 second)
            if pendingAudio.Count >= config.ChunkSizeMs * 16 then
                let audioToProcess = pendingAudio.ToArray()

                // Prepare parameters for streaming
                let mutable parameters = WhisperNative.WhisperFullParams()
                parameters.strategy <- 0  // GREEDY
                parameters.n_threads <- config.ThreadCount
                parameters.offset_ms <- 0  // Process from start of chunk
                parameters.duration_ms <- 0  // Process entire chunk
                parameters.translate <- config.EnableTranslate
                parameters.no_context <- false  // Use context from state
                parameters.single_segment <- false
                parameters.token_timestamps <- config.TokenTimestamps
                parameters.suppress_blank <- config.SuppressBlank
                parameters.suppress_non_speech_tokens <- config.SuppressNonSpeechTokens
                parameters.temperature <- config.Temperature
                parameters.temperature_inc <- config.TemperatureInc
                parameters.beam_size <- config.BeamSize
                parameters.best_of <- config.BestOf
                parameters.max_initial_ts <- config.MaxInitialTs
                parameters.length_penalty <- config.LengthPenalty

                // Set language
                match config.Language with
                | Some lang ->
                    parameters.language <- Marshal.StringToHGlobalAnsi(lang)
                    parameters.detect_language <- false
                | None ->
                    parameters.detect_language <- true

                // Set prompt tokens if provided
                match config.InitialPrompt with
                | Some prompt ->
                    let maxTokens = 512
                    let tokens = Array.zeroCreate<int> maxTokens
                    let tokenCount = WhisperNative.whisper_tokenize(ctx, prompt, tokens, maxTokens)
                    if tokenCount > 0 then
                        let tokenPtr = Marshal.AllocHGlobal(tokenCount * sizeof<int>)
                        Marshal.Copy(tokens, 0, tokenPtr, tokenCount)
                        parameters.prompt_tokens <- tokenPtr
                        parameters.prompt_n_tokens <- tokenCount
                | None -> ()

                // Set callbacks
                let callbackHandle = GCHandle.Alloc(newSegmentCallback)
                let encoderHandle = GCHandle.Alloc(encoderBeginCallback)
                parameters.new_segment_callback <- Marshal.GetFunctionPointerForDelegate(newSegmentCallback)
                parameters.encoder_begin_callback <- Marshal.GetFunctionPointerForDelegate(encoderBeginCallback)

                // Process with state for incremental transcription
                let result = WhisperNative.whisper_full_with_state(
                    ctx,
                    state,  // Use state for continuity
                    parameters,
                    audioToProcess,
                    audioToProcess.Length)

                // Clean up handles
                callbackHandle.Free()
                encoderHandle.Free()

                // Clear processed audio
                pendingAudio.Clear()

                if result = 0 then
                    // Get latest results from state
                    let segmentCount = WhisperNative.whisper_full_n_segments_from_state(state)
                    if segmentCount > 0 then
                        let lastSegment = segmentCount - 1
                        let textPtr = WhisperNative.whisper_full_get_segment_text_from_state(state, lastSegment)
                        let text = Marshal.PtrToStringAnsi(textPtr)

                        // Build complete transcription
                        let fullText = committedText + " " + text

                        return FinalTranscription(fullText, [], [])
                    else
                        return PartialTranscription("", [], 0.0f)
                else
                    return ProcessingError($"Whisper processing failed with code {result}")
            else
                // Not enough audio yet, just acknowledge
                return PartialTranscription("", [], 0.0f)

        with ex ->
            return ProcessingError(ex.Message)
    }
    
    /// Calculate text similarity for correction detection
    let calculateSimilarity (text1: string) (text2: string) =
        let len1 = text1.Length
        let len2 = text2.Length
        let maxLen = max len1 len2
        if maxLen = 0 then 1.0f
        else
            let minLen = min len1 len2
            let commonLen = 
                [0 .. minLen - 1]
                |> List.filter (fun i -> text1.[i] = text2.[i])
                |> List.length
            float32 commonLen / float32 maxLen
    
    interface IWhisperClient with
        member _.ProcessAsync(samples) =
            async {
                let! result = processChunkInternal samples
                match result with
                | PartialTranscription(text, tokens, conf) ->
                    return Ok {
                        FullText = text
                        Segments = []
                        Duration = TimeSpan.Zero
                        ProcessingTime = TimeSpan.Zero
                        Timestamp = DateTime.UtcNow
                        Language = config.Language
                        LanguageConfidence = Some conf
                        Tokens = Some tokens
                    }
                | FinalTranscription(text, tokens, segments) ->
                    return Ok {
                        FullText = text
                        Segments = segments
                        Duration = TimeSpan.Zero
                        ProcessingTime = TimeSpan.Zero
                        Timestamp = DateTime.UtcNow
                        Language = config.Language
                        LanguageConfidence = None
                        Tokens = Some tokens
                    }
                | ProcessingError msg ->
                    return Error (ProcessingError(0, msg))
                | _ ->
                    return Error (StateError "Unexpected state")
            }

        member _.ProcessStream(audioStream) =
            audioStream
            |> Observable.selectAsync (fun samples ->
                async {
                    let! result = processChunkInternal samples
                    return result
                })

        member _.ProcessFileAsync(path) =
            async {
                try
                    use stream = File.OpenRead(path)
                    let buffer = Array.zeroCreate<byte> (int stream.Length)
                    let! _ = stream.AsyncRead(buffer, 0, buffer.Length)
                    // Convert to float32 (implementation needed)
                    let samples = convertWavToFloat32 buffer
                    return! (this :> IWhisperClient).ProcessAsync(samples)
                with ex ->
                    return Error (ProcessingError(0, ex.Message))
            }

        member _.StreamingMode
            with get() = config.StreamingMode
            and set(value) = () // Update config

        member _.Events = events.Publish :> IObservable<_>

        member _.Reset() =
            WhisperNative.whisper_free_state(state)
            let newState = WhisperNative.whisper_init_state(ctx)
            // Update state reference
            previousSegmentCount <- 0
            committedText <- ""
            pendingAudio.Clear()

        member _.DetectLanguageAsync(samples) =
            async {
                try
                    let langProbs = Array.zeroCreate<float> 100
                    let langId = WhisperNative.whisper_lang_auto_detect(ctx, 0, config.ThreadCount, langProbs)
                    if langId >= 0 then
                        let langPtr = WhisperNative.whisper_lang_str(langId)
                        let lang = Marshal.PtrToStringAnsi(langPtr)
                        let probMap =
                            langProbs
                            |> Array.mapi (fun i p -> (getLanguageCode i, p))
                            |> Array.filter (fun (_, p) -> p > 0.0f)
                            |> Map.ofArray
                        return Ok {
                            Language = lang
                            Confidence = langProbs.[langId]
                            Probabilities = probMap
                        }
                    else
                        return Error (ProcessingError(langId, "Language detection failed"))
                with ex ->
                    return Error (ProcessingError(0, ex.Message))
            }

        member _.Dispose() =
            WhisperNative.whisper_free_state(state)
            WhisperNative.whisper_free(ctx)
```

### Functional Stream Processing Patterns

```fsharp
module StreamProcessing =
    open System
    open System.Reactive.Linq
    open FSharp.Control.Reactive
    
    /// Create a streaming transcription pipeline
    let createTranscriptionPipeline (audioSource: IObservable<float32[]>) (config: StreamConfig) =
        
        // Initialize whisper stream
        let whisperStream = new WhisperStream("models/ggml-base.en.bin", config)
        
        // Create processing pipeline
        audioSource
        // Buffer audio into chunks
        |> Observable.bufferTimeSpan (TimeSpan.FromMilliseconds(float config.ChunkSizeMs))
        |> Observable.map (Array.concat)
        
        // Process through whisper
        |> Observable.selectAsync (fun chunk -> whisperStream.ProcessChunk(chunk))
        
        // Filter based on confidence
        |> Observable.choose (function
            | PartialTranscription(text, _, conf) when conf >= config.MinConfidence -> 
                Some text
            | _ -> None)
        
        // Debounce rapid changes
        |> Observable.throttle (TimeSpan.FromMilliseconds 200.0)
    
    /// Smart text stabilization
    let stabilizeText (events: IObservable<TranscriptionEvent>) =
        events
        |> Observable.scan (fun (lastText, lastConf) event ->
            match event with
            | PartialTranscription(text, _, conf) ->
                // Only update if more confident or extending
                if conf > lastConf || text.StartsWith(lastText) then
                    (text, conf)
                else
                    (lastText, lastConf)
            | FinalTranscription(text, _, _) ->
                (text, 1.0f) // Final is always accepted
            | _ -> (lastText, lastConf)
        ) ("", 0.0f)
        |> Observable.map fst
        |> Observable.distinctUntilChanged
    
    /// Incremental typing with correction support
    type TypedTextState = {
        Committed: string  // Text already typed
        Pending: string     // Text waiting to be typed
        LastUpdate: DateTime
    }
    
    let createTypingPipeline (transcriptions: IObservable<string>) =
        transcriptions
        |> Observable.scan (fun state text ->
            // Check if we need to correct
            if text.StartsWith(state.Committed) then
                // Extension - just add pending
                { state with 
                    Pending = text.Substring(state.Committed.Length) 
                    LastUpdate = DateTime.UtcNow }
            else
                // Correction needed - mark for backspace
                let commonPrefix = 
                    Seq.zip state.Committed text
                    |> Seq.takeWhile (fun (a, b) -> a = b)
                    |> Seq.length
                
                { Committed = text.Substring(0, commonPrefix)
                  Pending = text.Substring(commonPrefix)
                  LastUpdate = DateTime.UtcNow }
        ) { Committed = ""; Pending = ""; LastUpdate = DateTime.UtcNow }
        
        // Emit typing commands
        |> Observable.map (fun state ->
            if state.Pending.Length > 0 then
                Some (TypeText state.Pending)
            else
                None)
        |> Observable.choose id
```

### Integration with Existing Codebase

Replacing current Whisper.NET usage:

#### Before (Whisper.NET):
```fsharp
// Old batch approach
let processRecording (audioBuffer: ResizeArray<float32>) =
    async {
        let samples = audioBuffer.ToArray()
        let! result = whisperProcessor.ProcessAsync(samples)
        typeText result.Text
    }
```

#### After (WhisperFS):
```fsharp
// New streaming approach
let processRecording (audioCapture: IAudioCapture) =
    // Create audio observable
    let audioStream = 
        audioCapture.AudioFrameAvailable
        |> Observable.map (fun frame -> frame.Samples)
    
    // Create transcription pipeline
    let transcriptions = 
        StreamProcessing.createTranscriptionPipeline audioStream streamConfig
        |> StreamProcessing.stabilizeText
    
    // Subscribe to type text incrementally
    transcriptions
    |> Observable.subscribe (fun text ->
        // Show in UI immediately
        updateUITranscription text
        
        // Type only when stable
        if isStableEnough text then
            typeText text)
```

### Advanced Features

#### 1. Voice Activity Detection Integration

```fsharp
type VADIntegratedStream(whisperStream: IWhisperStream, vadThreshold: float32) =
    
    let processWithVAD (samples: float32[]) =
        async {
            let energy = calculateEnergy samples
            
            if energy > vadThreshold then
                // Speech detected - process normally
                return! whisperStream.ProcessChunk(samples)
            else
                // Silence - might be end of sentence
                return PartialTranscription("", [], 0.0f)
        }
    
    member _.Process = processWithVAD
```

#### 2. Multi-language Support with Detection

```fsharp
let detectLanguageAndTranscribe (samples: float32[]) =
    async {
        // First pass: detect language
        let! detection = whisperStream.ProcessChunk(samples)
        
        match detection with
        | PartialTranscription(_, tokens, _) ->
            // Analyze tokens for language hints
            let language = detectLanguageFromTokens tokens
            
            // Second pass: transcribe with detected language
            whisperStream.SetLanguage(language)
            return! whisperStream.ProcessChunk(samples)
        | other -> return other
    }
```

#### 3. Punctuation and Formatting

```fsharp
module TextFormatting =
    
    /// Add punctuation based on prosody and pauses
    let addPunctuation (segments: Segment list) =
        segments
        |> List.map (fun segment ->
            let pauseAfter = 
                match segments |> List.tryFind (fun s -> s.StartTime > segment.EndTime) with
                | Some next -> next.StartTime - segment.EndTime
                | None -> 0.0f
            
            let punctuation =
                if pauseAfter > 1.0f then ". "
                elif pauseAfter > 0.5f then ", "
                else " "
            
            { segment with Text = segment.Text + punctuation })
    
    /// Apply capitalization rules
    let applyCapitalization (text: string) =
        let sentences = text.Split([|'. '; '? '; '! '|], StringSplitOptions.None)
        sentences
        |> Array.map (fun s -> 
            if s.Length > 0 then
                Char.ToUpper(s.[0]).ToString() + s.Substring(1)
            else s)
        |> String.concat ". "
```

## Performance Considerations

### Memory Management

```fsharp
module MemoryOptimization =
    open System.Buffers
    
    /// Use array pools for audio buffers
    let rentAudioBuffer (size: int) =
        ArrayPool<float32>.Shared.Rent(size)
    
    let returnAudioBuffer (buffer: float32[]) =
        ArrayPool<float32>.Shared.Return(buffer, true)
    
    /// Zero-copy audio processing
    [<Struct>]
    type AudioSpan = {
        Data: ReadOnlyMemory<float32>
        SampleRate: int
        Channels: int
    }
    
    let processAudioSpan (span: AudioSpan) =
        // Process without copying
        use pinned = span.Data.Pin()
        let ptr = pinned.Pointer
        // Pass directly to native code
        WhisperNative.whisper_full_with_state(ctx, parameters, ptr, span.Data.Length)
```

### Parallel Processing

```fsharp
/// Process multiple streams in parallel
let parallelTranscription (audioStreams: IObservable<float32[]> list) =
    audioStreams
    |> List.map (fun stream ->
        async {
            let whisper = new WhisperStream(modelPath, config)
            return! stream |> processStream whisper
        })
    |> Async.Parallel
```

## Error Handling and Recovery

```fsharp
/// Comprehensive error handling with Result type
module ErrorHandling =

    /// Convert native error codes to discriminated unions
    let mapNativeError (code: int) =
        match code with
        | -1 -> ModelLoadError "Failed to load model"
        | -2 -> InvalidAudioFormat "Invalid audio format"
        | -3 -> OutOfMemory
        | -4 -> StateError "Invalid state"
        | _ -> ProcessingError(code, $"Unknown error code: {code}")

    /// Retry logic with exponential backoff
    let retryWithBackoff<'T> (operation: unit -> Async<Result<'T, WhisperError>>) (maxRetries: int) =
        let rec retry attempt delay =
            async {
                match! operation() with
                | Ok result -> return Ok result
                | Error err when attempt < maxRetries ->
                    match err with
                    | OutOfMemory | ProcessingError _ ->
                        // Retryable errors
                        do! Async.Sleep delay
                        return! retry (attempt + 1) (delay * 2)
                    | _ ->
                        // Non-retryable errors
                        return Error err
                | Error err -> return Error err
            }
        retry 0 100

    /// Resource cleanup with error handling
    let useResource (acquire: unit -> 'T) (release: 'T -> unit) (action: 'T -> Async<Result<'R, WhisperError>>) =
        async {
            let resource = acquire()
            try
                return! action resource
            finally
                try release resource
                with _ -> () // Suppress cleanup errors
        }
```

## Testing Strategy

```fsharp
module Testing =
    open Xunit
    open FsUnit
    
    [<Fact>]
    let ``Stream should handle silence correctly`` () =
        // Arrange
        let silence = Array.zeroCreate<float32> 16000
        let stream = new WhisperStream(testModelPath, defaultConfig)
        
        // Act
        let result = stream.ProcessChunk(silence) |> Async.RunSynchronously
        
        // Assert
        match result with
        | PartialTranscription(text, _, _) ->
            text |> should equal ""
        | _ -> failwith "Expected empty transcription for silence"
    
    [<Fact>]
    let ``Stream should maintain context between chunks`` () =
        // Test that "Hello" + "world" produces "Hello world"
        let chunk1 = loadAudioFile "hello.wav"
        let chunk2 = loadAudioFile "world.wav"
        
        let stream = new WhisperStream(testModelPath, defaultConfig)
        
        let result1 = stream.ProcessChunk(chunk1) |> Async.RunSynchronously
        let result2 = stream.ProcessChunk(chunk2) |> Async.RunSynchronously
        
        match result2 with
        | PartialTranscription(text, _, _) ->
            text |> should contain "Hello world"
        | _ -> failwith "Expected combined transcription"
```

## Complete Feature Comparison with Whisper.NET

| Feature | Whisper.NET | WhisperFS | Notes |
|---------|-------------|------------|-------|
| **Model Management** | | | |
| Model downloading | ✅ WhisperGgmlDownloader | ✅ IModelDownloader | Full API compatibility |
| Model types (Tiny-Large) | ✅ GgmlType enum | ✅ ModelType DU | All sizes supported |
| Model from path | ✅ WhisperFactory.FromPath | ✅ IWhisperFactory.FromPath | Direct loading |
| Model from buffer | ❌ | ✅ IWhisperFactory.FromBuffer | Memory loading |
| **Processing Modes** | | | |
| Batch processing | ✅ ProcessAsync | ✅ IWhisperProcessor | Full compatibility |
| Streaming segments | ✅ IAsyncEnumerable | ✅ IAsyncEnumerable + Observable | Enhanced |
| Real-time streaming | ❌ | ✅ IWhisperStream | New capability |
| **Configuration** | | | |
| Language setting | ✅ WithLanguage | ✅ WithLanguage | Same API |
| Language detection | ❌ | ✅ DetectLanguageAsync | New feature |
| Translation mode | ✅ WithTranslate | ✅ WithTranslate | Full support |
| Thread count | ✅ WithThreads | ✅ WithThreads | Same API |
| GPU acceleration | ✅ Via runtime package | ✅ WithGpu | Integrated |
| Max segment length | ✅ | ✅ WithMaxSegmentLength | Same API |
| **Advanced Features** | | | |
| Token timestamps | ❌ | ✅ WithTokenTimestamps | New access |
| Token confidence | ❌ | ✅ Token.Probability | New data |
| Custom prompts | ❌ | ✅ WithPrompt | Style control |
| Beam search | ❌ | ✅ WithBeamSearch | Better accuracy |
| Temperature sampling | ❌ | ✅ WithTemperature | Control randomness |
| VAD integration | ❌ | ✅ Built-in VAD | Silence handling |
| **Results** | | | |
| Segment text | ✅ | ✅ | Same |
| Segment timing | ✅ TimeSpan | ✅ float32 + TimeSpan | Both formats |
| Full transcript | ✅ | ✅ | Same |
| Processing time | ✅ | ✅ | Same |
| Confidence scores | ❌ | ✅ Per segment/token | New metrics |
| **Resource Management** | | | |
| IDisposable | ✅ | ✅ | Same pattern |
| Memory efficiency | ❌ Buffers all | ✅ Streaming chunks | Improved |
| **API Patterns** | | | |
| Builder pattern | ✅ | ✅ Enhanced | Superset |
| Async/await | ✅ | ✅ | F# async |
| Observables | ❌ | ✅ | Reactive streams |

## Migration Path from Current Implementation

### Phase 1: Drop-in Replacement 
- Implement core Whisper.NET-compatible API surface
- Ensure 100% backward compatibility for PTT mode
- Run side-by-side testing with existing implementation
- Migration requires only namespace change

### Phase 2: Enhanced PTT Features 
- Enable token-level timestamps and confidence scores
- Add language detection for auto-language support
- Implement custom prompts for domain-specific vocabulary
- Add VAD for better silence handling in PTT mode

### Phase 3: Streaming Introduction
- Roll out experimental streaming mode
- A/B test streaming vs batch for user preference
- Tune stability thresholds based on feedback
- Monitor performance and accuracy metrics

### Phase 4: Full Migration
- Replace Whisper.NET completely
- Remove old dependencies from project
- Update documentation and examples
- Optimize based on real-world usage patterns

## Key Architectural Corrections from Review

Based on thorough analysis of whisper.cpp and Whisper.NET:

### Critical Implementation Requirements:

1. **Stateful Streaming**: Uses `whisper_state` object and `whisper_full_with_state` for true incremental processing
2. **Correct Timestamps**: Timestamps are in milliseconds, requiring `/1000.0f` conversion to seconds
3. **Proper Tokenization**: Prompts must be tokenized via `whisper_tokenize`, not passed as byte arrays
4. **Unified API**: Single `IWhisperClient` interface instead of separate processor/stream interfaces
5. **Native Library Management**: Automatic runtime selection based on platform capabilities (CUDA, AVX, CoreML)
6. **Result Types**: F# idiomatic error handling with `Result<'T, WhisperError>` instead of exceptions

### Implementation Highlights:

- **Streaming Callbacks**: Proper use of `new_segment_callback` and `encoder_begin_callback` for real-time updates
- **State Management**: Maintains `whisper_state` for context across chunks, enabling true streaming
- **Error Recovery**: Comprehensive error types with retry logic and resource cleanup
- **Platform Support**: Automatic detection and loading of optimal runtime (CUDA, CoreML, etc.)
- **Backward Compatibility**: Legacy `IWhisperProcessor` interface marked obsolete but still supported

## Conclusion

WhisperFS represents a comprehensive, architecturally sound unification of transcription capabilities. By correctly implementing whisper.cpp's streaming API with `whisper_state` and providing proper P/Invoke bindings, it delivers:

1. **True Streaming**: Incremental processing with state management, not repeated full processing
2. **Complete Feature Parity**: All Whisper.NET features plus access to advanced whisper.cpp capabilities
3. **Unified Architecture**: Single `IWhisperClient` interface adaptable to both batch and streaming modes
4. **Robust Error Handling**: F# idiomatic `Result` types with comprehensive error discrimination
5. **Automatic Runtime Selection**: Platform-aware loading of optimal native libraries
6. **Proper Resource Management**: Deterministic disposal with state cleanup

The design addresses all identified gaps from the review, ensuring correct use of whisper.cpp's streaming API while maintaining backward compatibility and providing a superior developer experience through F#'s functional programming paradigms.

### Next Steps

1. Prototype the core P/Invoke bindings
2. Implement basic streaming with a simple audio source
3. Test with real-world audio to tune parameters
4. Integrate with the existing Mel application
5. Gather user feedback and iterate

This design provides a solid foundation for building a production-ready streaming transcription system that learns from our past experiments and leverages the best of what whisper.cpp has to offer.