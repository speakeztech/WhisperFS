module WhisperFS.Tests.Core.StreamingTests

open System
open System.Reactive.Linq
open System.Reactive.Subjects
open System.Threading
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.Mocks
open WhisperFS.Tests.TestUtilities

[<Fact>]
let ``StreamingBuffer handles audio chunks correctly`` () =
    // Test streaming buffer functionality
    let buffer = RingBuffer<float32>(16000) // 1 second at 16kHz

    // Add audio chunks
    let chunk1 = AudioGenerator.generateSilence 100 16000
    let chunk2 = AudioGenerator.generateSineWave 440.0 100 16000

    buffer.Write(chunk1)
    buffer.Write(chunk2)

    buffer.Count |> should equal (chunk1.Length + chunk2.Length)

[<Fact>]
let ``Streaming handles overlapping chunks`` () =
    let chunkSizeMs = 1000
    let overlapMs = 200
    let sampleRate = 16000
    let chunkSizeSamples = (chunkSizeMs * sampleRate) / 1000
    let overlapSamples = (overlapMs * sampleRate) / 1000

    let buffer = RingBuffer<float32>(sampleRate * 10) // 10 seconds buffer

    // Simulate streaming with overlap
    let audioData = AudioGenerator.generateSpeechLike 3000 sampleRate
    buffer.Write(audioData)

    let mutable chunks = []
    let mutable position = 0

    while position + chunkSizeSamples <= buffer.Count do
        let chunk = buffer.Peek(chunkSizeSamples)
        chunks <- chunk :: chunks

        // Move forward by chunk size minus overlap
        let advance = chunkSizeSamples - overlapSamples
        buffer.Consume(advance)
        position <- position + advance

    chunks |> should not' (be Empty)

[<Fact>]
let ``TranscriptionEvent partial transcription contains confidence`` () =
    let event = TranscriptionEvent.PartialTranscription(
        "partial text",
        [{ Text = "partial"; Timestamp = 0.0f; Probability = 0.9f; IsSpecial = false }
         { Text = "text"; Timestamp = 0.5f; Probability = 0.85f; IsSpecial = false }],
        0.875f
    )

    match event with
    | PartialTranscription(text, tokens, confidence) ->
        text |> should equal "partial text"
        tokens |> should haveLength 2
        confidence |> should equal 0.875f
    | _ -> failwith "Wrong event type"

[<Fact>]
let ``TranscriptionEvent final transcription contains segments`` () =
    let segments = TestData.generateTestSegments 2 |> List.ofArray
    let event = TranscriptionEvent.FinalTranscription(
        "final text",
        [],
        segments
    )

    match event with
    | FinalTranscription(text, _, segs) ->
        text |> should equal "final text"
        segs |> should haveLength 2
    | _ -> failwith "Wrong event type"

[<Fact>]
let ``Observable stream emits events in order`` () =
    let subject = new Subject<Result<TranscriptionEvent, WhisperError>>()
    let events = ResizeArray<_>()

    use subscription = subject.Subscribe(events.Add)

    // Emit events in order
    subject.OnNext(Ok (PartialTranscription("one", [], 0.8f)))
    subject.OnNext(Ok (PartialTranscription("one two", [], 0.85f)))
    subject.OnNext(Ok (FinalTranscription("one two three", [], [])))

    Thread.Sleep(50)

    events.Count |> should equal 3

    // Verify order
    match events.[0] with
    | Ok (PartialTranscription(text, _, _)) -> text |> should equal "one"
    | _ -> failwith "Wrong first event"

    match events.[2] with
    | Ok (FinalTranscription(text, _, _)) -> text |> should equal "one two three"
    | _ -> failwith "Wrong last event"

[<Fact>]
let ``Stream handles errors gracefully`` () =
    let subject = new Subject<Result<TranscriptionEvent, WhisperError>>()
    let events = ResizeArray<_>()

    use subscription = subject.Subscribe(events.Add)

    // Mix successful and error events
    subject.OnNext(Ok (PartialTranscription("success", [], 0.9f)))
    subject.OnNext(Error (ProcessingError(1, "test error")))
    subject.OnNext(Ok (PartialTranscription("recovery", [], 0.85f)))

    Thread.Sleep(50)

    events.Count |> should equal 3

    // Check error event
    match events.[1] with
    | Error (ProcessingError(code, msg)) ->
        code |> should equal 1
        msg |> should equal "test error"
    | _ -> failwith "Expected error event"

[<Fact>]
let ``Stream throttling reduces event frequency`` () =
    let source = new Subject<int>()
    let throttled = source.Throttle(TimeSpan.FromMilliseconds(100.0))
    let events = ResizeArray<_>()

    use subscription = throttled.Subscribe(events.Add)

    // Send rapid events
    for i in 0 .. 9 do
        source.OnNext(i)
        Thread.Sleep(10)

    // Wait for throttle window
    Thread.Sleep(150)

    // Should only get the last value
    events.Count |> should equal 1
    events.[0] |> should equal 9

[<Fact>]
let ``Stream buffering collects events`` () =
    let source = new Subject<int>()
    let buffered = source.Buffer(TimeSpan.FromMilliseconds(100.0))
    let buffers = ResizeArray<_>()

    use subscription = buffered.Subscribe(buffers.Add)

    // Send events
    source.OnNext(1)
    source.OnNext(2)
    source.OnNext(3)

    // Wait for buffer window
    Thread.Sleep(150)

    buffers.Count |> should equal 1
    buffers.[0] |> should equal [1; 2; 3]

[<Fact>]
let ``Text stabilization filters unstable results`` () =
    let events = new Subject<TranscriptionEvent>()
    let mutable lastStableText = ""
    let stabilityThreshold = 0.8f

    use subscription =
        events
        |> Observable.choose (function
            | PartialTranscription(text, _, confidence) when confidence >= stabilityThreshold ->
                Some text
            | FinalTranscription(text, _, _) ->
                Some text
            | _ -> None)
        |> Observable.distinctUntilChanged
        |> Observable.subscribe (fun text -> lastStableText <- text)

    // Send events with varying confidence
    events.OnNext(PartialTranscription("unstable", [], 0.6f))
    events.OnNext(PartialTranscription("stable", [], 0.85f))
    events.OnNext(PartialTranscription("stable", [], 0.9f))  // Same text, should be filtered
    events.OnNext(FinalTranscription("final", [], []))

    Thread.Sleep(50)

    lastStableText |> should equal "final"

[<Fact>]
let ``Confidence calculation from tokens`` () =
    let tokens = [
        { Text = "hello"; Timestamp = 0.0f; Probability = 0.95f; IsSpecial = false }
        { Text = "world"; Timestamp = 0.5f; Probability = 0.85f; IsSpecial = false }
        { Text = "<|endoftext|>"; Timestamp = 1.0f; Probability = 1.0f; IsSpecial = true }
    ]

    let avgConfidence =
        tokens
        |> List.filter (fun t -> not t.IsSpecial)
        |> List.averageBy (fun t -> t.Probability)

    avgConfidence |> Assertions.shouldBeCloseTo 0.9f 0.01f

[<Fact>]
let ``Context update event preserves state`` () =
    let contextData = [| 1uy; 2uy; 3uy; 4uy |]
    let event = TranscriptionEvent.ContextUpdate(contextData)

    match event with
    | ContextUpdate data ->
        data |> should equal contextData
    | _ -> failwith "Wrong event type"

[<Fact>]
let ``Processing error event contains message`` () =
    let errorMsg = "Audio processing failed"
    let event = TranscriptionEvent.ProcessingError(errorMsg)

    match event with
    | ProcessingError msg ->
        msg |> should equal errorMsg
    | _ -> failwith "Wrong event type"

[<Fact>]
let ``Stream completes on disposal`` () =
    let subject = new Subject<int>()
    let mutable completed = false

    use subscription = subject.Subscribe(
        (fun _ -> ()),
        (fun _ -> ()),
        (fun () -> completed <- true)
    )

    subject.OnCompleted()
    Thread.Sleep(50)

    completed |> should be True

[<Fact>]
let ``Stream handles backpressure`` () =
    let source = new Subject<int>()
    let mutable processedCount = 0

    // Simulate slow consumer
    use subscription =
        source
        |> Observable.map (fun x ->
            Thread.Sleep(50)  // Slow processing
            x
        )
        |> Observable.subscribe (fun _ ->
            Interlocked.Increment(&processedCount) |> ignore
        )

    // Send events rapidly
    for i in 0 .. 9 do
        source.OnNext(i)

    source.OnCompleted()

    // Wait for processing
    Thread.Sleep(600)

    processedCount |> should equal 10

[<Fact>]
let ``Stream merging combines multiple sources`` () =
    let source1 = new Subject<string>()
    let source2 = new Subject<string>()

    let merged = Observable.Merge(source1, source2)
    let events = ResizeArray<_>()

    use subscription = merged.Subscribe(events.Add)

    source1.OnNext("from1")
    source2.OnNext("from2")
    source1.OnNext("from1-2")

    Thread.Sleep(50)

    events.Count |> should equal 3
    events |> should contain "from1"
    events |> should contain "from2"
    events |> should contain "from1-2"