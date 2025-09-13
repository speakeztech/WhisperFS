module WhisperFS.Tests.Performance.BenchmarkTests

open System
open System.Diagnostics
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.TestUtilities
open WhisperFS.Tests.TestUtilities.PerformanceHelpers

[<Fact>]
let ``Ring buffer write performance`` () =
    let buffer = RingBuffer<float32>(16000 * 60)  // 1 minute buffer
    let samples = AudioGenerator.generateSilence 16000 16000  // 1 second

    let result, elapsed = measureTime (fun () ->
        for _ in 0 .. 59 do
            buffer.Write(samples)
    )

    // Should complete in reasonable time
    elapsed.TotalMilliseconds |> should be (lessThan 100.0)

[<Fact>]
let ``Ring buffer read performance`` () =
    let buffer = RingBuffer<float32>(16000 * 60)
    let samples = AudioGenerator.generateSilence (16000 * 60) 16000
    buffer.Write(samples)

    let result, elapsed = measureTime (fun () ->
        let _ = buffer.Read(16000 * 60)
        ()
    )

    // Should complete quickly
    elapsed.TotalMilliseconds |> should be (lessThan 10.0)

[<Fact>]
let ``Audio generation performance`` () =
    let result = shouldCompleteWithinThreshold 100.0 (fun () ->
        AudioGenerator.generateSpeechLike 10000 16000
    )

    result |> should not' (be Null)

[<Fact>]
let ``Token confidence calculation performance`` () =
    let tokens = [
        for i in 0 .. 999 do
            yield {
                Text = sprintf "token%d" i
                Timestamp = float32 i * 0.1f
                Probability = 0.9f
                IsSpecial = false
            }
    ]

    let result, elapsed = measureTime (fun () ->
        tokens
        |> List.filter (fun t -> not t.IsSpecial)
        |> List.averageBy (fun t -> t.Probability)
    )

    // Should be fast even for many tokens
    elapsed.TotalMilliseconds |> should be (lessThan 10.0)

[<Fact>]
let ``Configuration validation performance`` () =
    let config = TestConfigBuilder().WithDefaults()

    let result, elapsed = measureTime (fun () ->
        for _ in 0 .. 999 do
            ErrorHandling.validateConfig config |> ignore
    )

    // Validation should be fast
    elapsed.TotalMilliseconds |> should be (lessThan 50.0)

[<Fact>]
let ``Model size calculation caching`` () =
    let model = ModelType.Base

    // First call might initialize
    let size1, elapsed1 = measureTime (fun () -> model.GetModelSize())

    // Subsequent calls should be cached/fast
    let size2, elapsed2 = measureTime (fun () ->
        let mutable total = 0L
        for _ in 0 .. 9999 do
            total <- total + model.GetModelSize()
        total
    )

    // Second batch should be much faster per operation
    let avgElapsed2 = elapsed2.TotalMilliseconds / 10000.0
    avgElapsed2 |> should be (lessThan 0.001)

[<Fact>]
let ``Concurrent buffer operations performance`` () =
    let buffer = RingBuffer<int>(10000)

    let elapsed = measureTimeAsync (async {
        let! _ =
            [| for i in 0 .. 9 do
                yield async {
                    for j in 0 .. 99 do
                        buffer.Write([| i * 100 + j |])
                } |]
            |> Async.Parallel

        return ()
    }) |> Async.RunSynchronously |> snd

    // Should handle concurrent access efficiently
    elapsed.TotalMilliseconds |> should be (lessThan 500.0)

[<Fact>]
let ``Memory allocation patterns`` () =
    let before = GC.GetTotalMemory(true)

    // Perform operations
    let buffer = RingBuffer<float32>(16000)
    for _ in 0 .. 99 do
        let samples = AudioGenerator.generateSilence 160 16000
        buffer.Write(samples)
        buffer.Consume(160)

    let after = GC.GetTotalMemory(true)
    let allocated = after - before

    // Should not leak significant memory
    // Ring buffer should reuse space
    allocated |> should be (lessThan (10L * 1024L * 1024L))  // Less than 10MB

[<Fact>]
let ``String operations performance`` () =
    let segments = TestData.generateTestSegments 100

    let result, elapsed = measureTime (fun () ->
        segments
        |> Array.map (fun s -> s.Text)
        |> String.concat " "
    )

    // String concatenation should be reasonably fast
    elapsed.TotalMilliseconds |> should be (lessThan 10.0)