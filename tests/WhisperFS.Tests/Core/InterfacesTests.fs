module WhisperFS.Tests.Core.InterfacesTests

open System
open System.IO
open System.Reactive.Linq
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.Mocks
open WhisperFS.Tests.TestUtilities

[<Fact>]
let ``IWhisperClient ProcessAsync returns transcription result`` () =
    let config = TestConfigBuilder().WithDefaults()
    let client = new MockWhisperClient(config) :> IWhisperClient

    let samples = AudioGenerator.generateSilence 1000 16000
    let result = client.ProcessAsync(samples) |> Async.RunSynchronously

    match result with
    | Ok transcription ->
        transcription.FullText |> should not' (be EmptyString)
        transcription.Segments |> should not' (be Empty)
    | Error err -> failwithf "Expected success but got error: %A" err

[<Fact>]
let ``IWhisperClient ProcessStream handles audio stream`` () =
    let config = TestConfigBuilder().WithStreaming(true).Build()
    let client = new MockWhisperClient(config) :> IWhisperClient
    let audioStream = new MockAudioStream()

    let events = ResizeArray<Result<TranscriptionEvent, WhisperError>>()
    use subscription = client.ProcessStream(audioStream.AsObservable()).Subscribe(events.Add)

    // Send audio data
    audioStream.SendAudio(AudioGenerator.generateSilence 100 16000)
    audioStream.SendAudio(AudioGenerator.generateSilence 100 16000)
    audioStream.Complete()

    // Wait for processing
    System.Threading.Thread.Sleep(100)

    events.Count |> should be (greaterThan 0)

[<Fact>]
let ``IWhisperClient ProcessFileAsync handles existing file`` () =
    use fixture = new TestFixture()
    let config = TestConfigBuilder().WithDefaults()
    let client = new MockWhisperClient(config) :> IWhisperClient

    // Create test WAV file
    let testFile = fixture.CreateTempFile("test.wav")
    let samples = AudioGenerator.generateSpeechLike 1000 16000
    FileHelpers.createTestWavFile testFile samples 16000

    let result = client.ProcessFileAsync(testFile) |> Async.RunSynchronously

    match result with
    | Ok transcription ->
        transcription.FullText |> should contain "test.wav"
    | Error err -> failwithf "Expected success but got error: %A" err

[<Fact>]
let ``IWhisperClient ProcessFileAsync returns error for missing file`` () =
    let config = TestConfigBuilder().WithDefaults()
    let client = new MockWhisperClient(config) :> IWhisperClient

    let result = client.ProcessFileAsync("/nonexistent/file.wav") |> Async.RunSynchronously

    match result with
    | Error (FileNotFound path) ->
        path |> should equal "/nonexistent/file.wav"
    | _ -> failwith "Expected FileNotFound error"

[<Fact>]
let ``IWhisperClient Process handles different input types`` () =
    let config = TestConfigBuilder().WithDefaults()
    let client = new MockWhisperClient(config) :> IWhisperClient

    // Test BatchAudio
    let batchInput = BatchAudio (AudioGenerator.generateSilence 100 16000)
    let batchOutput = client.Process(batchInput)

    match batchOutput with
    | BatchResult asyncResult ->
        let result = asyncResult |> Async.RunSynchronously
        result |> Assertions.shouldBeOk |> ignore
    | _ -> failwith "Expected BatchResult"

    // Test StreamingAudio
    let audioStream = new MockAudioStream()
    let streamInput = StreamingAudio (audioStream.AsObservable())
    let streamOutput = client.Process(streamInput)

    match streamOutput with
    | StreamingResult observable ->
        let events = ResizeArray<_>()
        use sub = observable.Subscribe(events.Add)
        audioStream.SendAudio(AudioGenerator.generateSilence 100 16000)
        audioStream.Complete()
        System.Threading.Thread.Sleep(50)
        events.Count |> should be (greaterThanOrEqualTo 1)
    | _ -> failwith "Expected StreamingResult"

[<Fact>]
let ``IWhisperClient Reset clears state`` () =
    let config = TestConfigBuilder().WithDefaults()
    let mockClient = new MockWhisperClient(config)
    let client = mockClient :> IWhisperClient

    // Process some audio
    let samples = AudioGenerator.generateSilence 100 16000
    client.ProcessAsync(samples) |> Async.RunSynchronously |> ignore

    // Reset
    let resetResult = client.Reset()
    resetResult |> Assertions.shouldBeOk

    mockClient.ResetCount |> should equal 1

[<Fact>]
let ``IWhisperClient DetectLanguageAsync identifies language`` () =
    let config = TestConfigBuilder().WithDefaults()
    let client = new MockWhisperClient(config) :> IWhisperClient

    let samples = AudioGenerator.generateSpeechLike 1000 16000
    let result = client.DetectLanguageAsync(samples) |> Async.RunSynchronously

    match result with
    | Ok detection ->
        detection.Language |> should equal "en"
        detection.Confidence |> should be (greaterThan 0.9f)
        detection.Probabilities.Count |> should be (greaterThan 0)
    | Error err -> failwithf "Expected success but got error: %A" err

[<Fact>]
let ``IWhisperClient GetMetrics returns performance data`` () =
    let config = TestConfigBuilder().WithDefaults()
    let client = new MockWhisperClient(config) :> IWhisperClient

    let metrics = client.GetMetrics()

    metrics.TotalProcessingTime |> should be (greaterThanOrEqualTo TimeSpan.Zero)
    metrics.SegmentsProcessed |> should be (greaterThanOrEqualTo 0)
    metrics.TokensGenerated |> should be (greaterThanOrEqualTo 0)
    metrics.ErrorCount |> should be (greaterThanOrEqualTo 0)

[<Fact>]
let ``IWhisperClient Events observable emits events`` () =
    let config = TestConfigBuilder().WithStreaming(true).Build()
    let mockClient = new MockWhisperClient(config)
    let client = mockClient :> IWhisperClient

    let events = ResizeArray<_>()
    use subscription = client.Events.Subscribe(events.Add)

    // Simulate events
    mockClient.SimulateEvent(PartialTranscription("test", [], 0.9f))
    mockClient.SimulateEvent(FinalTranscription("final", [], []))

    System.Threading.Thread.Sleep(50)

    events.Count |> should equal 2

[<Fact>]
let ``IWhisperClient is disposable`` () =
    let config = TestConfigBuilder().WithDefaults()
    let mockClient = new MockWhisperClient(config)
    let client = mockClient :> IWhisperClient

    client.Dispose()

    mockClient.IsDisposed |> should be True

[<Fact>]
let ``WhisperInput discriminated union works correctly`` () =
    let samples = AudioGenerator.generateSilence 100 16000
    let batchInput = BatchAudio samples

    match batchInput with
    | BatchAudio s ->
        s |> should equal samples
    | _ -> failwith "Wrong input type"

    let stream = Observable.Return(samples)
    let streamInput = StreamingAudio stream

    match streamInput with
    | StreamingAudio s ->
        s |> should equal stream
    | _ -> failwith "Wrong input type"

    let filePath = "/path/to/file.wav"
    let fileInput = AudioFile filePath

    match fileInput with
    | AudioFile p ->
        p |> should equal filePath
    | _ -> failwith "Wrong input type"

[<Fact>]
let ``WhisperOutput discriminated union works correctly`` () =
    let asyncResult = async { return Ok TestData.generateTestTranscriptionResult() }
    let batchOutput = BatchResult asyncResult

    match batchOutput with
    | BatchResult a ->
        let result = a |> Async.RunSynchronously
        result |> Assertions.shouldBeOk |> ignore
    | _ -> failwith "Wrong output type"

    let observable = Observable.Return(Ok (PartialTranscription("test", [], 0.9f)))
    let streamOutput = StreamingResult observable

    match streamOutput with
    | StreamingResult o ->
        let events = ResizeArray<_>()
        use sub = o.Subscribe(events.Add)
        System.Threading.Thread.Sleep(50)
        events.Count |> should equal 1
    | _ -> failwith "Wrong output type"

[<Fact>]
let ``IWhisperFactory creates clients`` () =
    let factory = new MockWhisperFactory() :> IWhisperFactory
    let config = TestConfigBuilder().WithDefaults()

    let client = factory.CreateClient(config)
    client |> should not' (be Null)

[<Fact>]
let ``IWhisperFactory FromPath loads model`` () =
    use fixture = new TestFixture()
    let factory = new MockWhisperFactory()

    // Create dummy model file
    let modelPath = fixture.CreateTempFile("model.bin")
    File.WriteAllBytes(modelPath, [| 0uy |])

    let result = factory.FromPath(modelPath)

    match result with
    | Ok f ->
        f |> should not' (be Null)
    | Error err -> failwithf "Expected success but got error: %A" err

[<Fact>]
let ``IWhisperFactory FromPath returns error for missing file`` () =
    let factory = new MockWhisperFactory()
    let result = factory.FromPath("/nonexistent/model.bin")

    match result with
    | Error (ModelLoadError msg) ->
        msg |> should contain "not found"
    | _ -> failwith "Expected ModelLoadError"

[<Fact>]
let ``IWhisperFactory FromBuffer loads from memory`` () =
    let factory = new MockWhisperFactory()
    let buffer = Array.zeroCreate<byte> 1024

    let result = factory.FromBuffer(buffer)

    match result with
    | Ok f ->
        f |> should not' (be Null)
    | Error err -> failwithf "Expected success but got error: %A" err

[<Fact>]
let ``IWhisperFactory GetModelInfo returns metadata`` () =
    let factory = new MockWhisperFactory() :> IWhisperFactory
    let info = factory.GetModelInfo()

    info.Type |> should equal ModelType.Base
    info.VocabSize |> should be (greaterThan 0)
    info.AudioContext |> should be (greaterThan 0)
    info.Languages |> should not' (be Empty)