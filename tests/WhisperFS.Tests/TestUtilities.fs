module WhisperFS.Tests.TestUtilities

open System
open System.IO
open WhisperFS
open FsUnit.Xunit

/// Test configuration builder
type TestConfigBuilder() =
    let mutable config = {
        ModelPath = ""
        ModelType = ModelType.Base
        Language = None
        ThreadCount = 4
        UseGpu = false
        EnableTranslate = false
        MaxSegmentLength = 30
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

    member _.WithDefaults() = config

    member _.WithModel(modelType: ModelType) =
        config <- { config with ModelType = modelType }
        this

    member _.WithStreaming(enabled: bool) =
        config <- { config with StreamingMode = enabled }
        this

    member _.WithLanguage(lang: string option) =
        config <- { config with Language = lang }
        this

    member _.Build() = config

/// Generate test audio samples
module AudioGenerator =
    /// Generate silent audio
    let generateSilence (durationMs: int) (sampleRate: int) =
        let sampleCount = (durationMs * sampleRate) / 1000
        Array.zeroCreate<float32> sampleCount

    /// Generate sine wave audio
    let generateSineWave (frequencyHz: float) (durationMs: int) (sampleRate: int) =
        let sampleCount = (durationMs * sampleRate) / 1000
        [| for i in 0 .. sampleCount - 1 do
            let t = float i / float sampleRate
            yield float32 (Math.Sin(2.0 * Math.PI * frequencyHz * t)) |]

    /// Generate white noise
    let generateWhiteNoise (durationMs: int) (sampleRate: int) =
        let random = Random()
        let sampleCount = (durationMs * sampleRate) / 1000
        [| for _ in 0 .. sampleCount - 1 do
            yield float32 (random.NextDouble() * 2.0 - 1.0) |]

    /// Generate audio with speech-like characteristics
    let generateSpeechLike (durationMs: int) (sampleRate: int) =
        let sampleCount = (durationMs * sampleRate) / 1000
        let random = Random()

        // Mix of frequencies typical in speech
        [| for i in 0 .. sampleCount - 1 do
            let t = float i / float sampleRate
            let fundamental = Math.Sin(2.0 * Math.PI * 200.0 * t)
            let harmonic1 = Math.Sin(2.0 * Math.PI * 400.0 * t) * 0.5
            let harmonic2 = Math.Sin(2.0 * Math.PI * 800.0 * t) * 0.3
            let noise = (random.NextDouble() - 0.5) * 0.1
            yield float32 (fundamental + harmonic1 + harmonic2 + noise) * 0.5f |]

/// File system helpers for tests
module FileHelpers =
    /// Create a temporary directory for test files
    let createTempDirectory() =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        Directory.CreateDirectory(path) |> ignore
        path

    /// Clean up temporary directory
    let cleanupDirectory (path: string) =
        if Directory.Exists(path) then
            Directory.Delete(path, true)

    /// Create a test WAV file
    let createTestWavFile (path: string) (samples: float32[]) (sampleRate: int) =
        use writer = new BinaryWriter(File.Create(path))

        // WAV header
        writer.Write("RIFF"B)
        writer.Write(36 + samples.Length * 2) // File size
        writer.Write("WAVE"B)
        writer.Write("fmt "B)
        writer.Write(16) // Subchunk size
        writer.Write(uint16 1) // Audio format (PCM)
        writer.Write(uint16 1) // Channels
        writer.Write(sampleRate) // Sample rate
        writer.Write(sampleRate * 2) // Byte rate
        writer.Write(uint16 2) // Block align
        writer.Write(uint16 16) // Bits per sample
        writer.Write("data"B)
        writer.Write(samples.Length * 2)

        // Write samples as 16-bit PCM
        for sample in samples do
            let pcm = int16 (sample * 32767.0f)
            writer.Write(pcm)

/// Assertion helpers
module Assertions =
    open Xunit

    /// Assert Result is Ok
    let shouldBeOk (result: Result<'T, 'E>) =
        match result with
        | Ok value -> value
        | Error err -> failwithf "Expected Ok but got Error: %A" err

    /// Assert Result is Error
    let shouldBeError (result: Result<'T, 'E>) =
        match result with
        | Ok value -> failwithf "Expected Error but got Ok: %A" value
        | Error err -> err

    /// Assert Result matches expected error
    let shouldBeErrorMatching (predicate: 'E -> bool) (result: Result<'T, 'E>) =
        match result with
        | Ok value -> failwithf "Expected Error but got Ok: %A" value
        | Error err ->
            if not (predicate err) then
                failwithf "Error did not match predicate: %A" err
            err

    /// Assert async completes within timeout
    let shouldCompleteWithin (timeoutMs: int) (computation: Async<'T>) =
        let cts = new System.Threading.CancellationTokenSource(timeoutMs)
        try
            Async.RunSynchronously(computation, cancellationToken = cts.Token)
        with
        | :? System.OperationCanceledException ->
            failwithf "Operation timed out after %d ms" timeoutMs

    /// Assert float values are approximately equal
    let shouldBeCloseTo (expected: float32) (tolerance: float32) (actual: float32) =
        let diff = abs (expected - actual)
        if diff > tolerance then
            failwithf "Expected %f ± %f but got %f (diff: %f)" expected tolerance actual diff

/// Observable test helpers
module ObservableHelpers =
    open System.Reactive.Linq
    open System.Reactive.Subjects
    open System.Collections.Generic

    /// Create a test subject for observables
    let createTestSubject<'T>() = new Subject<'T>()

    /// Collect all events from an observable
    let collectEvents (observable: IObservable<'T>) =
        let events = List<'T>()
        let subscription = observable.Subscribe(events.Add)
        events, subscription

    /// Wait for N events from an observable
    let waitForEvents (count: int) (timeoutMs: int) (observable: IObservable<'T>) =
        async {
            let events = ResizeArray<'T>()
            use semaphore = new System.Threading.SemaphoreSlim(0)

            use subscription =
                observable.Subscribe(fun event ->
                    events.Add(event)
                    if events.Count <= count then
                        semaphore.Release() |> ignore)

            // Wait for events
            for _ in 1 .. count do
                let! acquired =
                    semaphore.WaitAsync(timeoutMs)
                    |> Async.AwaitTask

                if not acquired then
                    failwithf "Timeout waiting for event %d of %d" events.Count count

            return events |> List.ofSeq
        }

/// Performance measurement helpers
module PerformanceHelpers =
    open System.Diagnostics

    /// Measure execution time
    let measureTime (action: unit -> 'T) =
        let sw = Stopwatch.StartNew()
        let result = action()
        sw.Stop()
        result, sw.Elapsed

    /// Measure async execution time
    let measureTimeAsync (computation: Async<'T>) =
        async {
            let sw = Stopwatch.StartNew()
            let! result = computation
            sw.Stop()
            return result, sw.Elapsed
        }

    /// Assert operation completes within performance threshold
    let shouldCompleteWithinThreshold (maxMs: float) (action: unit -> 'T) =
        let result, elapsed = measureTime action
        if elapsed.TotalMilliseconds > maxMs then
            failwithf "Operation took %.2f ms, expected less than %.2f ms"
                elapsed.TotalMilliseconds maxMs
        result

/// Test data providers
module TestData =
    /// Sample model types for testing
    let allModelTypes = [
        ModelType.Tiny
        ModelType.TinyEn
        ModelType.Base
        ModelType.BaseEn
        ModelType.Small
        ModelType.SmallEn
        ModelType.Medium
        ModelType.MediumEn
        ModelType.LargeV1
        ModelType.LargeV2
        ModelType.LargeV3
    ]

    /// Sample languages for testing
    let testLanguages = [
        Some "en"
        Some "es"
        Some "fr"
        Some "de"
        Some "zh"
        None // Auto-detect
    ]

    /// Sample error cases
    let testErrors = [
        WhisperError.ModelLoadError "Test model load error"
        WhisperError.ProcessingError(42, "Test processing error")
        WhisperError.InvalidAudioFormat "Test audio format error"
        WhisperError.StateError "Test state error"
        WhisperError.NativeLibraryError "Test native error"
        WhisperError.TokenizationError "Test tokenization error"
        WhisperError.OutOfMemory
        WhisperError.Cancelled
    ]

    /// Generate test segments
    let generateTestSegments (count: int) =
        [| for i in 0 .. count - 1 do
            yield {
                Text = sprintf "Test segment %d" i
                StartTime = float32 i * 1.0f
                EndTime = float32 i * 1.0f + 0.9f
                Tokens = [
                    { Text = "Test"; Timestamp = float32 i * 1.0f; Probability = 0.95f; IsSpecial = false }
                    { Text = "segment"; Timestamp = float32 i * 1.0f + 0.3f; Probability = 0.92f; IsSpecial = false }
                    { Text = string i; Timestamp = float32 i * 1.0f + 0.6f; Probability = 0.88f; IsSpecial = false }
                ]
            } |]

    /// Generate test transcription result
    let generateTestTranscriptionResult() =
        {
            FullText = "This is a test transcription with multiple segments."
            Segments = generateTestSegments 3 |> List.ofArray
            Duration = TimeSpan.FromSeconds(3.0)
            ProcessingTime = TimeSpan.FromMilliseconds(150.0)
            Timestamp = DateTime.UtcNow
            Language = Some "en"
            LanguageConfidence = Some 0.95f
            Tokens = None
        }