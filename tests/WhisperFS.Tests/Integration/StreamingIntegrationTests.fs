module WhisperFS.Tests.Integration.StreamingIntegrationTests

open System
open System.Reactive.Linq
open System.Threading
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.Mocks
open WhisperFS.Tests.TestUtilities
open WhisperFS.Tests.TestUtilities.ObservableHelpers

[<Fact>]
let ``Real-time streaming simulation`` () =
    let config = TestConfigBuilder()
                    .WithStreaming(true)
                    .WithModel(ModelType.Base)
                    .Build()

    let factory = new MockWhisperFactory()
    let client = factory.CreateClient(config)

    // Simulate real-time audio capture at 16kHz
    let audioSource =
        Observable.Interval(TimeSpan.FromMilliseconds(100.0))
        |> Observable.map (fun _ ->
            AudioGenerator.generateSpeechLike 1600 16000  // 100ms of audio
        )
        |> Observable.take 10  // 1 second total

    let events = ResizeArray<_>()
    use subscription = client.ProcessStream(audioSource).Subscribe(events.Add)

    // Wait for streaming to complete
    Thread.Sleep(1200)

    // Verify events were received
    events.Count |> should be (greaterThan 0)

    // Cleanup
    client.Dispose()

[<Fact>]
let ``Streaming with overlapping chunks`` () =
    let chunkSizeMs = 1000
    let overlapMs = 200
    let config = TestConfigBuilder()
                    .WithStreaming(true)
                    .Build()

    // Update config with chunk settings
    let streamConfig = { config with ChunkSizeMs = chunkSizeMs; OverlapMs = overlapMs }

    let factory = new MockWhisperFactory()
    let client = factory.CreateClient(streamConfig)

    let audioStream = new MockAudioStream()

    // Track events
    let events, subscription = collectEvents (client.ProcessStream(audioStream.AsObservable()))

    // Send overlapping audio chunks
    let fullAudio = AudioGenerator.generateSpeechLike 3000 16000
    let chunkSize = (chunkSizeMs * 16000) / 1000

    for i in 0 .. 2 do
        let startIdx = i * (chunkSize - (overlapMs * 16000 / 1000))
        let endIdx = min (startIdx + chunkSize) fullAudio.Length
        let chunk = fullAudio.[startIdx .. endIdx - 1]
        audioStream.SendAudio(chunk)
        Thread.Sleep(50)

    audioStream.Complete()
    Thread.Sleep(100)

    // Verify processing
    events.Count |> should be (greaterThan 0)

    // Cleanup
    subscription.Dispose()
    client.Dispose()

[<Fact>]
let ``Streaming text stabilization`` () =
    let config = TestConfigBuilder()
                    .WithStreaming(true)
                    .Build()

    let mockClient = new MockWhisperClient(config)
    let client = mockClient :> IWhisperClient

    // Create stabilized text stream
    let stabilizedText = ResizeArray<string>()

    use subscription =
        client.Events
        |> Observable.choose (function
            | Ok (PartialTranscription(text, _, confidence)) when confidence > 0.8f ->
                Some text
            | Ok (FinalTranscription(text, _, _)) ->
                Some text
            | _ -> None)
        |> Observable.distinctUntilChanged
        |> Observable.subscribe stabilizedText.Add

    // Simulate unstable and stable events
    mockClient.SimulateEvent(PartialTranscription("unst", [], 0.6f))  // Too low confidence
    mockClient.SimulateEvent(PartialTranscription("stable", [], 0.85f))
    mockClient.SimulateEvent(PartialTranscription("stable", [], 0.9f))  // Same text, filtered
    mockClient.SimulateEvent(PartialTranscription("stable text", [], 0.88f))
    mockClient.SimulateEvent(FinalTranscription("stable text final", [], []))

    Thread.Sleep(100)

    // Should have filtered duplicates and low confidence
    stabilizedText.Count |> should equal 3
    stabilizedText.[0] |> should equal "stable"
    stabilizedText.[1] |> should equal "stable text"
    stabilizedText.[2] |> should equal "stable text final"

    // Cleanup
    client.Dispose()

[<Fact>]
let ``Streaming buffering and batching`` () =
    let audioStream = new MockAudioStream()

    // Buffer audio into larger chunks
    let buffered =
        audioStream.AsObservable()
        |> Observable.bufferTimeSpan (TimeSpan.FromMilliseconds(500.0))
        |> Observable.map Array.concat

    let chunks = ResizeArray<float32[]>()
    use subscription = buffered.Subscribe(chunks.Add)

    // Send small chunks rapidly
    for i in 0 .. 9 do
        let smallChunk = AudioGenerator.generateSilence 100 16000
        audioStream.SendAudio(smallChunk)
        Thread.Sleep(50)

    audioStream.Complete()
    Thread.Sleep(600)

    // Should be batched into ~2 larger chunks
    chunks.Count |> should be (lessThanOrEqualTo 3)
    chunks |> Seq.sumBy (fun c -> c.Length) |> should equal 1000

[<Fact>]
let ``Streaming error handling and recovery`` () =
    let config = TestConfigBuilder().WithStreaming(true).Build()
    let mockClient = new MockWhisperClient(config)
    let client = mockClient :> IWhisperClient

    let successCount = ref 0
    let errorCount = ref 0

    use subscription =
        client.Events.Subscribe(function
            | Ok _ -> incr successCount
            | Error _ -> incr errorCount)

    // Mix successful and error events
    mockClient.SimulateEvent(PartialTranscription("success1", [], 0.9f))
    mockClient.SimulateError(ProcessingError(1, "error1"))
    mockClient.SimulateEvent(PartialTranscription("success2", [], 0.85f))
    mockClient.SimulateError(OutOfMemory)
    mockClient.SimulateEvent(FinalTranscription("final", [], []))

    Thread.Sleep(50)

    !successCount |> should equal 3
    !errorCount |> should equal 2

    // Cleanup
    client.Dispose()

[<Fact>]
let ``Streaming context preservation`` () =
    let config = TestConfigBuilder().WithStreaming(true).Build()
    let mockClient = new MockWhisperClient(config)
    let client = mockClient :> IWhisperClient

    let contextUpdates = ResizeArray<byte[]>()

    use subscription =
        client.Events
        |> Observable.choose (function
            | Ok (ContextUpdate data) -> Some data
            | _ -> None)
        |> Observable.subscribe contextUpdates.Add

    // Simulate context updates
    let context1 = [| 1uy; 2uy; 3uy |]
    let context2 = [| 4uy; 5uy; 6uy |]

    mockClient.SimulateEvent(ContextUpdate context1)
    mockClient.SimulateEvent(PartialTranscription("text", [], 0.9f))
    mockClient.SimulateEvent(ContextUpdate context2)

    Thread.Sleep(50)

    contextUpdates.Count |> should equal 2
    contextUpdates.[0] |> should equal context1
    contextUpdates.[1] |> should equal context2

    // Cleanup
    client.Dispose()

[<Fact>]
let ``Streaming performance under load`` () =
    let config = TestConfigBuilder().WithStreaming(true).Build()
    let factory = new MockWhisperFactory()
    let client = factory.CreateClient(config)

    // Generate high-frequency audio stream
    let audioSource =
        Observable.Interval(TimeSpan.FromMilliseconds(10.0))  // 100 Hz
        |> Observable.map (fun _ ->
            AudioGenerator.generateWhiteNoise 160 16000  // 10ms chunks
        )
        |> Observable.take 100  // 1 second total

    let eventCount = ref 0
    let startTime = DateTime.UtcNow

    use subscription =
        client.ProcessStream(audioSource)
        |> Observable.subscribe (fun _ -> incr eventCount)

    // Wait for completion
    Thread.Sleep(1500)

    let elapsed = DateTime.UtcNow - startTime

    // Should handle high-frequency input
    !eventCount |> should be (greaterThan 0)
    elapsed.TotalSeconds |> should be (lessThan 3.0)  // Should complete reasonably quickly

    // Cleanup
    client.Dispose()