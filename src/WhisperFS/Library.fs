namespace WhisperFS

open System

/// Main WhisperFS API entry point
module WhisperFS =

    /// Initialize WhisperFS with default settings
    let initialize() =
        // Initialize native library
        async {
            let! nativeResult = Native.Library.initializeAsync()
            match nativeResult with
            | Ok runtime ->
                printfn "WhisperFS initialized with runtime: %A" runtime
                return Ok()
            | Error err ->
                return Error err
        }

    /// Create a new whisper client
    let createClient (config: WhisperConfig) =
        // This would create the actual client combining Native and Runtime
        // For now, return a placeholder
        { new IWhisperClient with
            member _.ProcessAsync(samples) =
                async { return Error (NotImplemented "ProcessAsync") }

            member _.ProcessStream(audioStream) =
                audioStream |> Observable.map (fun _ -> Error (NotImplemented "ProcessStream"))

            member _.ProcessFileAsync(path) =
                async { return Error (NotImplemented "ProcessFileAsync") }

            member _.Process(input) =
                match input with
                | BatchAudio samples -> BatchResult (async { return Error (NotImplemented "BatchAudio") })
                | StreamingAudio stream -> StreamingResult (stream |> Observable.map (fun _ -> Error (NotImplemented "StreamingAudio")))
                | AudioFile path -> BatchResult (async { return Error (NotImplemented "AudioFile") })

            member _.Events =
                Observable.empty

            member _.Reset() =
                Ok()

            member _.DetectLanguageAsync(samples) =
                async { return Error (NotImplemented "DetectLanguageAsync") }

            member _.GetMetrics() =
                {
                    TotalProcessingTime = TimeSpan.Zero
                    TotalAudioProcessed = TimeSpan.Zero
                    AverageRealTimeFactor = 0.0
                    SegmentsProcessed = 0
                    TokensGenerated = 0
                    ErrorCount = 0
                }

            member _.Dispose() = ()
        }