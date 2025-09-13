module WhisperFS.Tests.CoreUtilsTests

open System
open System.Threading
open Xunit
open FsUnit.Xunit
open WhisperFS

/// Tests for RingBuffer functionality
[<Collection("Sequential")>] // RingBuffer uses locks, run sequentially
type RingBufferTests() =

    [<Fact>]
    member _.``RingBuffer initializes with correct capacity and state``() =
        let buffer = RingBuffer<int>(100)

        buffer.Capacity |> should equal 100
        buffer.Count |> should equal 0
        buffer.IsEmpty |> should be True
        buffer.IsFull |> should be False

    [<Fact>]
    member _.``RingBuffer.Write adds items correctly``() =
        let buffer = RingBuffer<float32>(10)
        let data = [| 1.0f; 2.0f; 3.0f |]

        buffer.Write(data)

        buffer.Count |> should equal 3
        buffer.IsEmpty |> should be False
        buffer.IsFull |> should be False

    [<Fact>]
    member _.``RingBuffer.Read returns items without consuming``() =
        let buffer = RingBuffer<string>(10)
        let data = [| "a"; "b"; "c"; "d"; "e" |]

        buffer.Write(data)
        let readItems = buffer.Read(3)

        readItems |> should equal [| "a"; "b"; "c" |]
        buffer.Count |> should equal 5 // Not consumed

    [<Fact>]
    member _.``RingBuffer.Read handles request for more items than available``() =
        let buffer = RingBuffer<int>(10)
        buffer.Write([| 1; 2; 3 |])

        let readItems = buffer.Read(10) // Request more than available

        readItems |> should equal [| 1; 2; 3 |]
        readItems.Length |> should equal 3

    [<Fact>]
    member _.``RingBuffer.Consume removes items from buffer``() =
        let buffer = RingBuffer<char>(10)
        buffer.Write([| 'a'; 'b'; 'c'; 'd'; 'e' |])

        buffer.Consume(2)

        buffer.Count |> should equal 3
        let remaining = buffer.Read(5)
        remaining |> should equal [| 'c'; 'd'; 'e' |]

    [<Fact>]
    member _.``RingBuffer.Consume handles consuming more than available``() =
        let buffer = RingBuffer<int>(10)
        buffer.Write([| 1; 2; 3 |])

        buffer.Consume(10) // Try to consume more than available

        buffer.Count |> should equal 0
        buffer.IsEmpty |> should be True

    [<Fact>]
    member _.``RingBuffer handles circular wrapping correctly``() =
        let buffer = RingBuffer<int>(5)

        // Fill the buffer
        buffer.Write([| 1; 2; 3; 4; 5 |])
        buffer.IsFull |> should be True

        // Consume some items
        buffer.Consume(3)
        buffer.Count |> should equal 2

        // Write more (should wrap around)
        buffer.Write([| 6; 7; 8 |])
        buffer.Count |> should equal 5

        // Verify correct order
        let items = buffer.ToArray()
        items |> should equal [| 4; 5; 6; 7; 8 |]

    [<Fact>]
    member _.``RingBuffer overwrites oldest data when full``() =
        let buffer = RingBuffer<int>(3)

        // Overfill the buffer
        buffer.Write([| 1; 2; 3; 4; 5; 6 |])

        buffer.Count |> should equal 3
        buffer.IsFull |> should be True
        let items = buffer.ToArray()
        items |> should equal [| 4; 5; 6 |] // Oldest items overwritten

    [<Fact>]
    member _.``RingBuffer.Clear resets the buffer``() =
        let buffer = RingBuffer<float>(10)
        buffer.Write([| 1.0; 2.0; 3.0; 4.0; 5.0 |])

        buffer.Clear()

        buffer.Count |> should equal 0
        buffer.IsEmpty |> should be True
        buffer.IsFull |> should be False
        buffer.ToArray() |> should equal [||]

    [<Fact>]
    member _.``RingBuffer.ToArray returns items in correct order``() =
        let buffer = RingBuffer<string>(10)
        buffer.Write([| "first"; "second"; "third" |])

        let array = buffer.ToArray()

        array |> should equal [| "first"; "second"; "third" |]
        array.Length |> should equal 3

    [<Fact>]
    member _.``RingBuffer is thread-safe for concurrent operations``() =
        let buffer = RingBuffer<int>(100)
        let mutable writeCount = 0
        let mutable readCount = 0

        // Start multiple writer threads
        let writers =
            [1..5] |> List.map (fun i ->
                async {
                    for j in 1..20 do
                        buffer.Write([| i * 100 + j |])
                        Interlocked.Increment(&writeCount) |> ignore
                        do! Async.Sleep(1)
                })

        // Start multiple reader threads
        let readers =
            [1..3] |> List.map (fun _ ->
                async {
                    for _ in 1..10 do
                        let items = buffer.Read(5)
                        Interlocked.Add(&readCount, items.Length) |> ignore
                        buffer.Consume(items.Length)
                        do! Async.Sleep(2)
                })

        // Run all threads
        let allTasks = writers @ readers
        allTasks |> Async.Parallel |> Async.RunSynchronously |> ignore

        // Verify operations completed without crashes
        writeCount |> should equal 100
        // Read count varies due to timing, just verify some reads happened
        readCount |> should be (greaterThan 0)

/// Tests for AudioUtils functionality
type AudioUtilsTests() =

    [<Fact>]
    member _.``calculateEnergy computes mean squared correctly``() =
        let samples = [| 0.5f; -0.5f; 0.5f; -0.5f |]

        let energy = AudioUtils.calculateEnergy samples

        // Mean of squares: (0.25 + 0.25 + 0.25 + 0.25) / 4 = 0.25
        energy |> should (equalWithin 0.001) 0.25f

    [<Fact>]
    member _.``calculateEnergy handles silence``() =
        let samples = Array.create 1000 0.0f

        let energy = AudioUtils.calculateEnergy samples

        energy |> should equal 0.0f

    [<Fact>]
    member _.``calculateEnergy handles empty array``() =
        let samples = [||]

        let energy = AudioUtils.calculateEnergy samples

        energy |> should equal 0.0f

    [<Fact>]
    member _.``calculateEnergy handles single sample``() =
        let samples = [| 0.8f |]

        let energy = AudioUtils.calculateEnergy samples

        energy |> should (equalWithin 0.001) 0.64f // 0.8^2

    [<Fact>]
    member _.``convertWavToFloat32 handles valid WAV header``() =
        // Create a minimal valid WAV file (44-byte header + 4 bytes of data)
        let wavBytes = Array.zeroCreate<byte> 48

        // RIFF header
        "RIFF".ToCharArray() |> Array.iteri (fun i c -> wavBytes.[i] <- byte c)
        // File size - 8
        BitConverter.GetBytes(40) |> Array.iteri (fun i b -> wavBytes.[4 + i] <- b)
        // WAVE format
        "WAVE".ToCharArray() |> Array.iteri (fun i c -> wavBytes.[8 + i] <- byte c)
        // fmt subchunk
        "fmt ".ToCharArray() |> Array.iteri (fun i c -> wavBytes.[12 + i] <- byte c)
        // Subchunk size (16 for PCM)
        BitConverter.GetBytes(16) |> Array.iteri (fun i b -> wavBytes.[16 + i] <- b)
        // Audio format (1 = PCM)
        BitConverter.GetBytes(1s) |> Array.iteri (fun i b -> wavBytes.[20 + i] <- b)
        // Num channels (1 = mono)
        BitConverter.GetBytes(1s) |> Array.iteri (fun i b -> wavBytes.[22 + i] <- b)
        // Sample rate (16000 Hz)
        BitConverter.GetBytes(16000) |> Array.iteri (fun i b -> wavBytes.[24 + i] <- b)
        // Byte rate
        BitConverter.GetBytes(32000) |> Array.iteri (fun i b -> wavBytes.[28 + i] <- b)
        // Block align
        BitConverter.GetBytes(2s) |> Array.iteri (fun i b -> wavBytes.[32 + i] <- b)
        // Bits per sample
        BitConverter.GetBytes(16s) |> Array.iteri (fun i b -> wavBytes.[34 + i] <- b)
        // data subchunk
        "data".ToCharArray() |> Array.iteri (fun i c -> wavBytes.[36 + i] <- byte c)
        // Data size
        BitConverter.GetBytes(4) |> Array.iteri (fun i b -> wavBytes.[40 + i] <- b)

        // Add two 16-bit samples
        BitConverter.GetBytes(16383s) |> Array.iteri (fun i b -> wavBytes.[44 + i] <- b) // ~0.5
        BitConverter.GetBytes(-16383s) |> Array.iteri (fun i b -> wavBytes.[46 + i] <- b) // ~-0.5

        let samples = AudioUtils.convertWavToFloat32 wavBytes

        samples.Length |> should equal 2
        samples.[0] |> should (equalWithin 0.01) 0.5f
        samples.[1] |> should (equalWithin 0.01) -0.5f

    [<Fact>]
    member _.``convertWavToFloat32 throws on invalid WAV``() =
        let invalidWav = Array.create 20 0uy // Too small

        (fun () -> AudioUtils.convertWavToFloat32 invalidWav |> ignore)
        |> should throw typeof<Exception>

    [<Fact>]
    member _.``lowPassFilter attenuates high frequencies``() =
        // Create a high-frequency signal (alternating +1, -1)
        let samples = Array.init 100 (fun i -> if i % 2 = 0 then 1.0f else -1.0f)

        let filtered = AudioUtils.lowPassFilter samples 100.0f 16000.0f

        // Filtered signal should have lower energy
        let originalEnergy = AudioUtils.calculateEnergy samples
        let filteredEnergy = AudioUtils.calculateEnergy filtered

        filteredEnergy |> should be (lessThan originalEnergy)

    [<Fact>]
    member _.``lowPassFilter preserves DC component``() =
        // DC signal (constant value)
        let samples = Array.create 100 0.7f

        let filtered = AudioUtils.lowPassFilter samples 1000.0f 16000.0f

        // DC should pass through mostly unchanged
        let average = filtered |> Array.average
        average |> should (equalWithin 0.05) 0.7f

    [<Fact>]
    member _.``lowPassFilter handles single sample``() =
        let samples = [| 0.5f |]

        let filtered = AudioUtils.lowPassFilter samples 1000.0f 16000.0f

        // Single sample should pass through
        filtered.Length |> should equal 1
        filtered.[0] |> should equal 0.5f

/// Tests for EventAggregator functionality
type EventAggregatorTests() =

    [<Fact>]
    member _.``EventAggregator triggers events to subscribers``() =
        let aggregator = EventAggregator<string>()
        let mutable received = []

        use _ = aggregator.Subscribe(fun msg ->
            received <- msg :: received)

        aggregator.Trigger("first")
        aggregator.Trigger("second")
        aggregator.Trigger("third")

        received |> List.rev |> should equal ["first"; "second"; "third"]

    [<Fact>]
    member _.``EventAggregator supports multiple subscribers``() =
        let aggregator = EventAggregator<int>()
        let mutable sum1 = 0
        let mutable sum2 = 0

        use _ = aggregator.Subscribe(fun n -> sum1 <- sum1 + n)
        use _ = aggregator.Subscribe(fun n -> sum2 <- sum2 + n * 2)

        aggregator.Trigger(5)
        aggregator.Trigger(10)

        sum1 |> should equal 15
        sum2 |> should equal 30

    [<Fact>]
    member _.``EventAggregator stops notifying after disposal``() =
        let aggregator = EventAggregator<string>()
        let mutable count = 0

        let subscription = aggregator.Subscribe(fun _ -> count <- count + 1)

        aggregator.Trigger("one")
        count |> should equal 1

        subscription.Dispose()

        aggregator.Trigger("two")
        count |> should equal 1 // Should not increase

    [<Fact>]
    member _.``EventAggregator.Clear removes all subscribers``() =
        let aggregator = EventAggregator<float>()
        let mutable total = 0.0

        use _ = aggregator.Subscribe(fun n -> total <- total + n)
        use _ = aggregator.Subscribe(fun n -> total <- total + n * 2.0)

        aggregator.Trigger(10.0)
        total |> should equal 30.0

        aggregator.Clear()

        aggregator.Trigger(10.0)
        total |> should equal 30.0 // Should not change

    [<Fact>]
    member _.``EventAggregator is thread-safe``() =
        let aggregator = EventAggregator<int>()
        let mutable total = 0

        use _ = aggregator.Subscribe(fun n ->
            Interlocked.Add(&total, n) |> ignore)

        // Trigger events from multiple threads
        [1..100]
        |> List.map (fun i -> async {
            aggregator.Trigger(i)
        })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore

        // Sum of 1..100 = 5050
        total |> should equal 5050

/// Tests for PerformanceMonitor functionality
type PerformanceMonitorTests() =

    [<Fact>]
    member _.``PerformanceMonitor tracks processing time``() =
        PerformanceMonitor.reset()

        PerformanceMonitor.start()
        Thread.Sleep(50) // Simulate work
        PerformanceMonitor.stop()

        let metrics = PerformanceMonitor.getMetrics()
        metrics.TotalProcessingTime.TotalMilliseconds |> should be (greaterThan 40.0)
        metrics.TotalProcessingTime.TotalMilliseconds |> should be (lessThan 200.0)

    [<Fact>]
    member _.``PerformanceMonitor accumulates audio processed``() =
        PerformanceMonitor.reset()

        PerformanceMonitor.addAudioProcessed(TimeSpan.FromSeconds(1.5))
        PerformanceMonitor.addAudioProcessed(TimeSpan.FromSeconds(2.5))
        PerformanceMonitor.addAudioProcessed(TimeSpan.FromSeconds(0.5))

        let metrics = PerformanceMonitor.getMetrics()
        metrics.TotalAudioProcessed.TotalSeconds |> should (equalWithin 0.001) 4.5

    [<Fact>]
    member _.``PerformanceMonitor counts segments and tokens``() =
        PerformanceMonitor.reset()

        PerformanceMonitor.addSegment()
        PerformanceMonitor.addSegment()
        PerformanceMonitor.addSegment()

        PerformanceMonitor.addTokens(10)
        PerformanceMonitor.addTokens(15)
        PerformanceMonitor.addTokens(5)

        let metrics = PerformanceMonitor.getMetrics()
        metrics.SegmentsProcessed |> should equal 3
        metrics.TokensGenerated |> should equal 30

    [<Fact>]
    member _.``PerformanceMonitor tracks error count``() =
        PerformanceMonitor.reset()

        PerformanceMonitor.addError()
        PerformanceMonitor.addError()

        let metrics = PerformanceMonitor.getMetrics()
        metrics.ErrorCount |> should equal 2

    [<Fact>]
    member _.``PerformanceMonitor calculates real-time factor``() =
        PerformanceMonitor.reset()

        PerformanceMonitor.start()
        PerformanceMonitor.addAudioProcessed(TimeSpan.FromSeconds(10.0))
        Thread.Sleep(100) // Process for 100ms
        PerformanceMonitor.stop()

        let metrics = PerformanceMonitor.getMetrics()
        // RTF = processing time / audio time
        // Should be around 0.01 (0.1s / 10s)
        metrics.AverageRealTimeFactor |> should be (lessThan 0.5)
        metrics.AverageRealTimeFactor |> should be (greaterThan 0.0)

    [<Fact>]
    member _.``PerformanceMonitor.reset clears all metrics``() =
        // Add some data
        PerformanceMonitor.start()
        Thread.Sleep(10)
        PerformanceMonitor.stop()
        PerformanceMonitor.addAudioProcessed(TimeSpan.FromSeconds(5.0))
        PerformanceMonitor.addSegment()
        PerformanceMonitor.addTokens(100)
        PerformanceMonitor.addError()

        // Reset
        PerformanceMonitor.reset()

        let metrics = PerformanceMonitor.getMetrics()
        metrics.TotalProcessingTime |> should equal TimeSpan.Zero
        metrics.TotalAudioProcessed |> should equal TimeSpan.Zero
        metrics.SegmentsProcessed |> should equal 0
        metrics.TokensGenerated |> should equal 0
        metrics.ErrorCount |> should equal 0
        metrics.AverageRealTimeFactor |> should equal 0.0

    [<Fact>]
    member _.``PerformanceMonitor handles no audio processed``() =
        PerformanceMonitor.reset()

        PerformanceMonitor.start()
        Thread.Sleep(10)
        PerformanceMonitor.stop()
        // Don't add any audio

        let metrics = PerformanceMonitor.getMetrics()
        metrics.AverageRealTimeFactor |> should equal 0.0 // Should handle division by zero