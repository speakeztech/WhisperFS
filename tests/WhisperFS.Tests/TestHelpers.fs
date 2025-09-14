module WhisperFS.Tests.TestHelpers

open System
open WhisperFS

/// Pure functions for generating test data
module TestData =

    /// Generate test configuration with valid defaults
    let defaultTestConfig = {
        ModelPath = "/test/model.bin"
        ModelType = ModelType.Base
        Language = Some "en"
        ChunkSizeMs = 1000
        Strategy = SamplingStrategy.Greedy
        ThreadCount = 4
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

    /// Generate audio samples for testing
    let generateSilence (durationMs: int) (sampleRate: int) =
        let sampleCount = (durationMs * sampleRate) / 1000
        Array.zeroCreate<float32> sampleCount

    /// Generate sine wave audio for testing
    let generateTone (frequencyHz: float) (durationMs: int) (sampleRate: int) =
        let sampleCount = (durationMs * sampleRate) / 1000
        [| for i in 0 .. sampleCount - 1 do
            let t = float i / float sampleRate
            yield float32 (Math.Sin(2.0 * Math.PI * frequencyHz * t)) |]

    /// Sample model types for testing
    let allModelTypes = [
        ModelType.Tiny; ModelType.TinyEn
        ModelType.Base; ModelType.BaseEn
        ModelType.Small; ModelType.SmallEn
        ModelType.Medium; ModelType.MediumEn
        ModelType.LargeV1; ModelType.LargeV2; ModelType.LargeV3
    ]

    /// Generate test transcription result
    let testResult = {
        FullText = "Hello world"
        Duration = TimeSpan.FromSeconds(2.0)
        ProcessingTime = TimeSpan.FromMilliseconds(100.0)
        Timestamp = DateTime.UtcNow
        Language = Some "en"
        LanguageConfidence = Some 0.9f
        Tokens = None
        Segments = [
            { Text = "Hello"; StartTime = 0.0f; EndTime = 0.5f; Tokens = []; SpeakerTurnNext = false }
            { Text = " world"; StartTime = 0.5f; EndTime = 2.0f; Tokens = []; SpeakerTurnNext = false }
        ]
    }

/// Result assertion helpers
module Assertions =
    open FsUnit.Xunit

    let shouldBeOk result =
        match result with
        | Ok _ -> ()
        | Error e -> failwithf "Expected Ok but got Error: %A" e

    let shouldBeError result =
        match result with
        | Error _ -> ()
        | Ok v -> failwithf "Expected Error but got Ok: %A" v

    let getOkValue result =
        match result with
        | Ok value -> value
        | Error e -> failwithf "Expected Ok but got Error: %A" e