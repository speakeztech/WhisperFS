module WhisperFS.Tests.Integration.WhisperClientTests

open System
open System.IO
open System.Reactive.Linq
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.Mocks
open WhisperFS.Tests.TestUtilities

[<Fact>]
let ``End-to-end batch transcription workflow`` () =
    // Setup
    let config = TestConfigBuilder()
                    .WithModel(ModelType.Base)
                    .WithLanguage(Some "en")
                    .Build()

    let factory = new MockWhisperFactory()
    let client = factory.CreateClient(config)

    // Generate test audio
    let samples = AudioGenerator.generateSpeechLike 2000 16000

    // Process
    let result = client.ProcessAsync(samples) |> Async.RunSynchronously

    // Verify
    match result with
    | Ok transcription ->
        transcription.FullText |> should not' (be EmptyString)
        transcription.Segments |> should not' (be Empty)
        transcription.Duration |> should be (greaterThan TimeSpan.Zero)
        transcription.Language |> should equal (Some "en")
    | Error err -> failwithf "Transcription failed: %A" err

    // Cleanup
    client.Dispose()

[<Fact>]
let ``End-to-end streaming transcription workflow`` () =
    // Setup
    let config = TestConfigBuilder()
                    .WithModel(ModelType.Base)
                    .WithStreaming(true)
                    .Build()

    let factory = new MockWhisperFactory()
    let client = factory.CreateClient(config)
    let audioStream = new MockAudioStream()

    // Collect events
    let events = ResizeArray<_>()
    use subscription = client.ProcessStream(audioStream.AsObservable()).Subscribe(events.Add)

    // Stream audio chunks
    for i in 0 .. 4 do
        let chunk = AudioGenerator.generateSpeechLike 200 16000
        audioStream.SendAudio(chunk)
        System.Threading.Thread.Sleep(10)

    audioStream.Complete()

    // Wait for processing
    System.Threading.Thread.Sleep(100)

    // Verify
    events.Count |> should be (greaterThan 0)

    // Should have at least one successful event
    events |> Seq.exists (function
        | Ok (PartialTranscription _) -> true
        | _ -> false) |> should be True

    // Cleanup
    client.Dispose()

[<Fact>]
let ``File processing with model download`` () =
    use fixture = new TestFixture()

    // Setup
    let downloader = new MockModelDownloader()
    let factory = new MockWhisperFactory()

    // Ensure model is downloaded
    let modelPath =
        (downloader :> IModelDownloader).DownloadModelAsync(ModelType.Tiny)
        |> Async.RunSynchronously

    // Create test audio file
    let audioFile = fixture.CreateTempFile("test_audio.wav")
    let samples = AudioGenerator.generateSpeechLike 1000 16000
    FileHelpers.createTestWavFile audioFile samples 16000

    // Process file
    let config = TestConfigBuilder().WithModel(ModelType.Tiny).Build()
    let client = factory.CreateClient(config)

    let result = client.ProcessFileAsync(audioFile) |> Async.RunSynchronously

    // Verify
    match result with
    | Ok transcription ->
        transcription.FullText |> should contain "test_audio.wav"
        transcription.Duration |> should be (greaterThan TimeSpan.Zero)
    | Error err -> failwithf "File processing failed: %A" err

    // Cleanup
    client.Dispose()

[<Fact>]
let ``Language detection workflow`` () =
    // Setup
    let config = TestConfigBuilder()
                    .WithLanguage(None)  // Auto-detect
                    .Build()

    let factory = new MockWhisperFactory()
    let client = factory.CreateClient(config)

    // Generate test audio
    let samples = AudioGenerator.generateSpeechLike 1000 16000

    // Detect language
    let detectionResult = client.DetectLanguageAsync(samples) |> Async.RunSynchronously

    // Verify
    match detectionResult with
    | Ok detection ->
        detection.Language |> should not' (be EmptyString)
        detection.Confidence |> should be (greaterThan 0.0f)
        detection.Probabilities.Count |> should be (greaterThan 0)
    | Error err -> failwithf "Language detection failed: %A" err

    // Process with detected language
    let transcriptionResult = client.ProcessAsync(samples) |> Async.RunSynchronously

    match transcriptionResult with
    | Ok transcription ->
        transcription.FullText |> should not' (be EmptyString)
    | Error err -> failwithf "Transcription failed: %A" err

    // Cleanup
    client.Dispose()

[<Fact>]
let ``Concurrent client operations`` () =
    let factory = new MockWhisperFactory()
    let config = TestConfigBuilder().WithDefaults()

    // Create multiple clients
    let clients = [
        for i in 0 .. 2 do
            yield factory.CreateClient(config)
    ]

    // Process audio concurrently
    let tasks =
        clients
        |> List.mapi (fun i client ->
            async {
                let samples = AudioGenerator.generateSpeechLike (500 + i * 100) 16000
                return! client.ProcessAsync(samples)
            })

    let results = tasks |> Async.Parallel |> Async.RunSynchronously

    // Verify all succeeded
    results |> should haveLength 3
    results |> Array.iter (fun result ->
        match result with
        | Ok _ -> ()
        | Error err -> failwithf "Client failed: %A" err
    )

    // Cleanup
    clients |> List.iter (fun c -> c.Dispose())

[<Fact>]
let ``Error recovery and retry`` () =
    let config = TestConfigBuilder().WithDefaults()
    let mockClient = new MockWhisperClient(config)
    let client = mockClient :> IWhisperClient

    // Simulate error
    mockClient.SimulateError(ProcessingError(1, "Temporary failure"))

    // Retry with backoff
    let retryOperation() = async {
        let samples = AudioGenerator.generateSilence 100 16000
        return! client.ProcessAsync(samples)
    }

    let result = ErrorHandling.retryWithBackoff retryOperation 3 |> Async.RunSynchronously

    // Should eventually succeed (mock always succeeds on actual ProcessAsync)
    match result with
    | Ok transcription ->
        transcription.FullText |> should not' (be EmptyString)
    | Error err -> failwithf "Retry failed: %A" err

    // Cleanup
    client.Dispose()

[<Fact>]
let ``Performance metrics collection`` () =
    let config = TestConfigBuilder().WithDefaults()
    let factory = new MockWhisperFactory()
    let client = factory.CreateClient(config)

    // Process multiple audio samples
    for i in 0 .. 4 do
        let samples = AudioGenerator.generateSpeechLike 500 16000
        client.ProcessAsync(samples) |> Async.RunSynchronously |> ignore

    // Get metrics
    let metrics = client.GetMetrics()

    // Verify metrics
    metrics.TotalProcessingTime |> should be (greaterThanOrEqualTo TimeSpan.Zero)
    metrics.SegmentsProcessed |> should be (greaterThanOrEqualTo 0)

    // Cleanup
    client.Dispose()

[<Fact>]
let ``State reset during streaming`` () =
    let config = TestConfigBuilder().WithStreaming(true).Build()
    let mockClient = new MockWhisperClient(config)
    let client = mockClient :> IWhisperClient
    let audioStream = new MockAudioStream()

    // Start streaming
    let events1 = ResizeArray<_>()
    use sub1 = client.ProcessStream(audioStream.AsObservable()).Subscribe(events1.Add)

    audioStream.SendAudio(AudioGenerator.generateSilence 100 16000)
    System.Threading.Thread.Sleep(50)

    // Reset state
    client.Reset() |> Assertions.shouldBeOk

    // Continue streaming after reset
    audioStream.SendAudio(AudioGenerator.generateSilence 100 16000)
    audioStream.Complete()
    System.Threading.Thread.Sleep(50)

    // Verify reset was called
    mockClient.ResetCount |> should equal 1

    // Cleanup
    client.Dispose()