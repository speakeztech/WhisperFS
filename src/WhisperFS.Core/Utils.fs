namespace WhisperFS

open System
open System.Threading

/// Ring buffer implementation for bounded audio storage
type RingBuffer<'T>(capacity: int) =
    let buffer = Array.zeroCreate<'T> capacity
    let mutable head = 0
    let mutable tail = 0
    let mutable count = 0
    let lockObj = obj()

    member _.Capacity = capacity
    member _.Count = lock lockObj (fun () -> count)
    member _.IsFull = lock lockObj (fun () -> count = capacity)
    member _.IsEmpty = lock lockObj (fun () -> count = 0)

    member _.Write(items: 'T[]) =
        lock lockObj (fun () ->
            for item in items do
                buffer.[tail] <- item
                tail <- (tail + 1) % capacity

                if count < capacity then
                    count <- count + 1
                else
                    // Overwrite oldest data
                    head <- (head + 1) % capacity
        )

    member _.Read(maxItems: int) =
        lock lockObj (fun () ->
            let itemsToRead = min maxItems count
            let result = Array.zeroCreate<'T> itemsToRead

            for i in 0 .. itemsToRead - 1 do
                result.[i] <- buffer.[(head + i) % capacity]

            result
        )

    member _.Consume(itemsToConsume: int) =
        lock lockObj (fun () ->
            let itemsToRemove = min itemsToConsume count
            head <- (head + itemsToRemove) % capacity
            count <- count - itemsToRemove
        )

    member _.ToArray() =
        lock lockObj (fun () ->
            let result = Array.zeroCreate<'T> count
            for i in 0 .. count - 1 do
                result.[i] <- buffer.[(head + i) % capacity]
            result
        )

    member _.Clear() =
        lock lockObj (fun () ->
            head <- 0
            tail <- 0
            count <- 0
        )

/// Audio utilities
module AudioUtils =

    /// Convert WAV bytes to float32 samples
    let convertWavToFloat32 (wavBytes: byte[]) =
        // Simple WAV parsing - assumes 16-bit PCM
        if wavBytes.Length < 44 then
            failwith "Invalid WAV file"

        // Skip WAV header (44 bytes for standard WAV)
        let dataStart = 44
        let dataLength = wavBytes.Length - dataStart
        let sampleCount = dataLength / 2  // 16-bit samples

        let samples = Array.zeroCreate<float32> sampleCount

        for i in 0 .. sampleCount - 1 do
            let byteIndex = dataStart + (i * 2)
            let sample = BitConverter.ToInt16(wavBytes, byteIndex)
            samples.[i] <- float32 sample / 32768.0f  // Normalize to [-1, 1]

        samples

    /// Calculate audio energy for VAD
    let calculateEnergy (samples: float32[]) =
        if samples.Length = 0 then
            0.0f
        else
            samples
            |> Array.sumBy (fun s -> s * s)
            |> fun sum -> sum / float32 samples.Length

    /// Apply simple low-pass filter for noise reduction
    let lowPassFilter (samples: float32[]) (cutoffFreq: float32) (sampleRate: float32) =
        let rc = 1.0f / (cutoffFreq * 2.0f * float32 Math.PI)
        let dt = 1.0f / sampleRate
        let alpha = dt / (rc + dt)

        let filtered = Array.zeroCreate<float32> samples.Length
        filtered.[0] <- samples.[0]

        for i in 1 .. samples.Length - 1 do
            filtered.[i] <- filtered.[i-1] + alpha * (samples.[i] - filtered.[i-1])

        filtered

/// Thread-safe event aggregator
type EventAggregator<'T>() =
    let event = Event<'T>()
    let lockObj = obj()
    let mutable subscribers = []

    member _.Publish = event.Publish

    member _.Trigger(value: 'T) =
        lock lockObj (fun () ->
            event.Trigger(value)
        )

    member _.Subscribe(handler: 'T -> unit) =
        lock lockObj (fun () ->
            let subscription = event.Publish.Subscribe(handler)
            subscribers <- subscription :: subscribers
            subscription
        )

    member _.Clear() =
        lock lockObj (fun () ->
            for sub in subscribers do
                sub.Dispose()
            subscribers <- []
        )

// Performance tracking removed - was only used in commented-out Streaming.fs
// If needed in future, should use the PerformanceMetrics type from Types.fs

