module WhisperFS.Tests.Core.UtilsTests

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open WhisperFS

[<Fact>]
let ``RingBuffer initializes with correct capacity`` () =
    let buffer = RingBuffer<int>(10)

    buffer.Capacity |> should equal 10
    buffer.Count |> should equal 0
    buffer.IsEmpty |> should be True
    buffer.IsFull |> should be False

[<Fact>]
let ``RingBuffer Write adds items correctly`` () =
    let buffer = RingBuffer<int>(5)

    buffer.Write([| 1; 2; 3 |])

    buffer.Count |> should equal 3
    buffer.IsEmpty |> should be False
    buffer.IsFull |> should be False

[<Fact>]
let ``RingBuffer overwrites oldest data when full`` () =
    let buffer = RingBuffer<int>(3)

    // Fill buffer
    buffer.Write([| 1; 2; 3 |])
    buffer.IsFull |> should be True

    // Overwrite with new data
    buffer.Write([| 4; 5 |])

    // Should still be full
    buffer.Count |> should equal 3
    buffer.IsFull |> should be True

    // Read all data - should have newest items
    let data = buffer.Read(3)
    data |> should equal [| 3; 4; 5 |]

[<Fact>]
let ``RingBuffer Read returns requested items`` () =
    let buffer = RingBuffer<string>(10)

    buffer.Write([| "a"; "b"; "c"; "d"; "e" |])

    let data = buffer.Read(3)
    data |> should equal [| "a"; "b"; "c" |]

    // Count should not change after read
    buffer.Count |> should equal 5

[<Fact>]
let ``RingBuffer Read returns available items when less than requested`` () =
    let buffer = RingBuffer<int>(10)

    buffer.Write([| 1; 2; 3 |])

    let data = buffer.Read(5)
    data |> should equal [| 1; 2; 3 |]

[<Fact>]
let ``RingBuffer Consume removes items correctly`` () =
    let buffer = RingBuffer<int>(10)

    buffer.Write([| 1; 2; 3; 4; 5 |])
    buffer.Consume(2)

    buffer.Count |> should equal 3

    let remaining = buffer.Read(3)
    remaining |> should equal [| 3; 4; 5 |]

[<Fact>]
let ``RingBuffer Consume handles consuming more than available`` () =
    let buffer = RingBuffer<int>(10)

    buffer.Write([| 1; 2; 3 |])
    buffer.Consume(5) // Try to consume more than available

    buffer.Count |> should equal 0
    buffer.IsEmpty |> should be True

[<Fact>]
let ``RingBuffer ToArray returns all items in order`` () =
    let buffer = RingBuffer<char>(5)

    buffer.Write([| 'a'; 'b'; 'c' |])

    let array = buffer.ToArray()
    array |> should equal [| 'a'; 'b'; 'c' |]

[<Fact>]
let ``RingBuffer Clear removes all items`` () =
    let buffer = RingBuffer<int>(10)

    buffer.Write([| 1; 2; 3; 4; 5 |])
    buffer.Clear()

    buffer.Count |> should equal 0
    buffer.IsEmpty |> should be True

[<Fact>]
let ``RingBuffer handles circular wraparound correctly`` () =
    let buffer = RingBuffer<int>(4)

    // Fill buffer
    buffer.Write([| 1; 2; 3; 4 |])

    // Consume some items
    buffer.Consume(2)

    // Add more items (will wrap around)
    buffer.Write([| 5; 6 |])

    // Should have [3, 4, 5, 6]
    let data = buffer.ToArray()
    data |> should equal [| 3; 4; 5; 6 |]

[<Fact>]
let ``RingBuffer is thread-safe for concurrent writes`` () =
    let buffer = RingBuffer<int>(1000)
    let numThreads = 10
    let itemsPerThread = 50

    let tasks = [|
        for i in 0 .. numThreads - 1 do
            yield Task.Run(fun () ->
                let items = [| for j in 0 .. itemsPerThread - 1 do yield i * 100 + j |]
                buffer.Write(items)
            )
    |]

    Task.WaitAll(tasks)

    // Should have exactly the capacity (last 1000 items written)
    buffer.Count |> should equal 1000
    buffer.IsFull |> should be True

[<Fact>]
let ``RingBuffer is thread-safe for concurrent reads and writes`` () =
    let buffer = RingBuffer<int>(100)
    let mutable writeCount = 0
    let mutable readCount = 0

    let writerTask = Task.Run(fun () ->
        for i in 0 .. 199 do
            buffer.Write([| i |])
            Interlocked.Increment(&writeCount) |> ignore
            Thread.Sleep(1)
    )

    let readerTask = Task.Run(fun () ->
        while readCount < 100 do
            if buffer.Count > 0 then
                let data = buffer.Read(1)
                buffer.Consume(1)
                Interlocked.Increment(&readCount) |> ignore
            Thread.Sleep(1)
    )

    Task.WaitAll([| writerTask; readerTask |])

    readCount |> should be (greaterThanOrEqualTo 100)

[<Theory>]
[<InlineData(1)>]
[<InlineData(10)>]
[<InlineData(100)>]
[<InlineData(1000)>]
let ``RingBuffer works with different capacities`` (capacity: int) =
    let buffer = RingBuffer<float32>(capacity)

    buffer.Capacity |> should equal capacity

    // Fill to capacity
    let data = Array.init capacity (fun i -> float32 i)
    buffer.Write(data)

    buffer.Count |> should equal capacity
    buffer.IsFull |> should be True

    // Verify data
    let retrieved = buffer.ToArray()
    retrieved |> should equal data

[<Fact>]
let ``RingBuffer handles empty reads correctly`` () =
    let buffer = RingBuffer<string>(10)

    let data = buffer.Read(5)
    data |> should be Empty

    let array = buffer.ToArray()
    array |> should be Empty

[<Fact>]
let ``RingBuffer Peek returns items without consuming`` () =
    let buffer = RingBuffer<int>(10)

    buffer.Write([| 1; 2; 3; 4; 5 |])

    let peeked = buffer.Peek(3)
    peeked |> should equal [| 1; 2; 3 |]

    // Count should remain unchanged
    buffer.Count |> should equal 5

    // Can still read the same items
    let read = buffer.Read(3)
    read |> should equal [| 1; 2; 3 |]

[<Fact>]
let ``RingBuffer handles audio samples correctly`` () =
    // Simulate audio buffer for 1 second at 16kHz
    let sampleRate = 16000
    let buffer = RingBuffer<float32>(sampleRate)

    // Generate and write audio samples
    let samples = Array.init (sampleRate / 2) (fun i ->
        float32 (Math.Sin(2.0 * Math.PI * 440.0 * float i / float sampleRate))
    )

    buffer.Write(samples)
    buffer.Count |> should equal (sampleRate / 2)

    // Read in chunks (simulate streaming)
    let chunkSize = 1600 // 100ms chunks
    let mutable chunks = []

    while buffer.Count >= chunkSize do
        let chunk = buffer.Read(chunkSize)
        buffer.Consume(chunkSize)
        chunks <- chunk :: chunks

    chunks |> should haveLength 5