namespace WhisperFS

open System
open System.IO
open System.Runtime.InteropServices
open System.Reactive.Linq
open System.Threading
open WhisperFS.Native
open FSharp.Control.Reactive

/// Implementation of IWhisperClient using native whisper.cpp
type WhisperClient(context: WhisperContext.Context, config: WhisperConfig) =
    let mutable disposed = false
    let mutable state: WhisperContext.State option = None
    let mutable metrics = {
        TotalProcessingTime = TimeSpan.Zero
        TotalAudioProcessed = TimeSpan.Zero
        AverageRealTimeFactor = 0.0
        SegmentsProcessed = 0
        TokensGenerated = 0
        ErrorCount = 0
    }

    let events = new System.Reactive.Subjects.Subject<Result<TranscriptionEvent, WhisperError>>()

    let getOrCreateState() =
        match state with
        | Some s when not s.IsDisposed -> Ok s
        | _ ->
            match WhisperContext.createState context with
            | Ok s ->
                state <- Some s
                Ok s
            | Error e -> Error e

    let getParameters() =
        // Get the strategy as an integer
        let strategyInt =
            match config.Strategy with
            | Greedy -> WhisperSamplingStrategy.WHISPER_SAMPLING_GREEDY
            | BeamSearch(_, _) -> WhisperSamplingStrategy.WHISPER_SAMPLING_BEAM_SEARCH

        // Get default parameters for the strategy
        let mutable parameters = WhisperContext.getDefaultParams strategyInt

        // Convert language string to IntPtr
        let languagePtr =
            match config.Language with
            | Some lang ->
                let langBytes = System.Text.Encoding.UTF8.GetBytes(lang + "\x00")
                let ptr = Marshal.AllocHGlobal(langBytes.Length)
                Marshal.Copy(langBytes, 0, ptr, langBytes.Length)
                ptr
            | None -> IntPtr.Zero

        // Set fields on the mutable struct
        parameters.strategy <- strategyInt
        parameters.n_threads <- config.ThreadCount
        parameters.n_max_text_ctx <- config.MaxTextContext
        parameters.offset_ms <- config.OffsetMs
        parameters.duration_ms <- config.DurationMs
        parameters.translate <- config.Translate
        parameters.no_context <- config.NoContext
        parameters.no_timestamps <- config.NoTimestamps
        parameters.single_segment <- config.SingleSegment
        parameters.print_special <- config.PrintSpecial
        parameters.print_progress <- config.PrintProgress
        parameters.print_realtime <- config.PrintRealtime
        parameters.print_timestamps <- config.PrintTimestamps

        // Token-level timestamps
        parameters.token_timestamps <- config.TokenTimestamps
        parameters.thold_pt <- config.ThresholdPt
        parameters.thold_ptsum <- config.ThresholdPtSum
        parameters.max_len <- config.MaxLen
        parameters.split_on_word <- config.SplitOnWord
        parameters.max_tokens <- config.MaxTokens

        // Temperature and thresholds
        parameters.temperature <- config.Temperature
        parameters.temperature_inc <- config.TemperatureInc
        parameters.entropy_thold <- config.EntropyThreshold
        parameters.logprob_thold <- config.LogProbThreshold
        parameters.no_speech_thold <- config.NoSpeechThreshold
        parameters.max_initial_ts <- config.MaxInitialTs
        parameters.length_penalty <- config.LengthPenalty

        // Beam search parameters if applicable
        match config.Strategy with
        | BeamSearch(beamSize, bestOf) ->
            parameters.beam_size <- beamSize
            parameters.best_of <- bestOf
        | _ ->
            parameters.best_of <- 1

        // Language and detection
        parameters.language <- languagePtr
        parameters.detect_language <- config.DetectLanguage

        // Suppression
        parameters.suppress_blank <- config.SuppressBlank
        parameters.suppress_non_speech_tokens <- config.SuppressNonSpeech

        parameters

    let freeLanguagePtr (parameters: WhisperFullParams) =
        if parameters.language <> IntPtr.Zero then
            Marshal.FreeHGlobal(parameters.language)

    let processWithTiming (processFunc: unit -> Async<Result<TranscriptionResult, WhisperError>>) =
        async {
            let startTime = DateTime.UtcNow
            events.OnNext(Ok (PartialTranscription("Processing started", [], 1.0f)))

            let! result = processFunc()

            let elapsed = DateTime.UtcNow - startTime
            metrics <- { metrics with TotalProcessingTime = metrics.TotalProcessingTime + elapsed }

            match result with
            | Ok transcription ->
                events.OnNext(Ok (FinalTranscription(transcription.FullText, transcription.Tokens |> Option.defaultValue [], transcription.Segments)))
            | Error e ->
                metrics <- { metrics with ErrorCount = metrics.ErrorCount + 1 }
                events.OnNext(Error e)

            return result
        }

    interface IWhisperClient with
        member _.ProcessAsync(samples) =
            async {
                let stopwatch = System.Diagnostics.Stopwatch.StartNew()

                let! result = processWithTiming (fun () ->
                    async {
                        if disposed then
                            return Error (StateError "Client is disposed")
                        else
                            let parameters = getParameters()
                            try
                                // Process audio
                                let! processResult = WhisperContext.processAudio context parameters samples

                                match processResult with
                                | Ok () ->
                                    // Get segments
                                    match WhisperContext.getSegments context with
                                    | Ok segments ->
                                        metrics <-
                                            { metrics with
                                                SegmentsProcessed = metrics.SegmentsProcessed + segments.Length
                                                TotalAudioProcessed = metrics.TotalAudioProcessed + TimeSpan.FromSeconds(float samples.Length / 16000.0) }

                                        // Capture processing time
                                        let processingTime = stopwatch.Elapsed

                                        // Build result
                                        let result = {
                                            FullText = segments |> Array.map (fun s -> s.Text) |> String.concat " "
                                            Segments = segments |> Array.toList
                                            Duration = TimeSpan.FromSeconds(float samples.Length / 16000.0)
                                            ProcessingTime = processingTime
                                            Timestamp = DateTime.UtcNow
                                            Language = config.Language
                                            LanguageConfidence = None
                                            Tokens = Some (segments |> Array.collect (fun s -> s.Tokens |> List.toArray) |> Array.toList)
                                        }

                                        return Ok result
                                    | Error e ->
                                        return Error e
                                | Error e ->
                                    return Error e
                            finally
                                freeLanguagePtr parameters
                    })

                stopwatch.Stop()
                return result
            }

        member _.ProcessStream(audioStream) =
            audioStream |> Observable.bind (fun samples ->
                Observable.Create(fun (observer: IObserver<Result<TranscriptionEvent, WhisperError>>) ->
                    let task = async {
                        if disposed then
                            observer.OnNext(Error (StateError "Client is disposed"))
                        else
                            match getOrCreateState() with
                            | Error e ->
                                observer.OnNext(Error e)
                            | Ok streamState ->
                                let mutable parameters = getParameters()
                                parameters.single_segment <- true // Process incrementally

                                try
                                    let! processResult = WhisperContext.processAudioWithState streamState parameters samples

                                    match processResult with
                                    | Ok () ->
                                        match WhisperContext.getSegmentsFromState streamState with
                                        | Ok segments ->
                                            for segment in segments do
                                                let event = PartialTranscription(segment.Text, segment.Tokens, 0.95f)
                                                observer.OnNext(Ok event)
                                        | Error e ->
                                            observer.OnNext(Error e)
                                    | Error e ->
                                        observer.OnNext(Error e)
                                finally
                                    freeLanguagePtr parameters
                    }

                    Async.Start task
                    Action(fun () -> ())
                ))

        member _.ProcessFileAsync(path) =
            async {
                if disposed then
                    return Error (StateError "Client is disposed")
                else if not (File.Exists(path)) then
                    return Error (FileNotFound path)
                else
                    try
                        // Read audio file and convert to samples
                        // This would need proper audio file reading/decoding
                        // For now, return not implemented
                        return Error (NotImplemented "Audio file reading not yet implemented")
                    with
                    | ex -> return Error (NativeLibraryError ex.Message)
            }

        member this.Process(input) =
            match input with
            | BatchAudio samples ->
                BatchResult ((this :> IWhisperClient).ProcessAsync(samples))
            | StreamingAudio stream ->
                StreamingResult ((this :> IWhisperClient).ProcessStream(stream))
            | AudioFile path ->
                BatchResult ((this :> IWhisperClient).ProcessFileAsync(path))

        member _.Events = events :> IObservable<Result<TranscriptionEvent, WhisperError>>

        member _.Reset() =
            if disposed then
                Error (StateError "Client is disposed")
            else
                // Dispose current state if any
                state |> Option.iter (fun s -> (s :> IDisposable).Dispose())
                state <- None

                // Reset metrics
                metrics <- {
                    TotalProcessingTime = TimeSpan.Zero
                    TotalAudioProcessed = TimeSpan.Zero
                    AverageRealTimeFactor = 0.0
                    SegmentsProcessed = 0
                    TokensGenerated = 0
                    ErrorCount = 0
                }

                Ok()

        member _.DetectLanguageAsync(samples) =
            async {
                if disposed then
                    return Error (StateError "Client is disposed")
                else
                    return! WhisperContext.detectLanguage context samples
            }

        member _.GetMetrics() = metrics

        member _.Dispose() =
            if not disposed then
                disposed <- true
                state |> Option.iter (fun s -> (s :> IDisposable).Dispose())
                events.Dispose()

/// Factory for creating WhisperClient instances
module WhisperClientFactory =

    /// Create a client from a model path
    let createFromPath (modelPath: string) (config: WhisperConfig) =
        async {
            match! WhisperContext.loadModel modelPath with
            | Ok context ->
                return Ok (new WhisperClient(context, config) :> IWhisperClient)
            | Error e ->
                return Error e
        }

    /// Create a client with custom context parameters
    let createWithParams (modelPath: string) (contextParams: WhisperContextParams) (config: WhisperConfig) =
        async {
            match! WhisperContext.loadModelWithParams modelPath contextParams with
            | Ok context ->
                return Ok (new WhisperClient(context, config) :> IWhisperClient)
            | Error e ->
                return Error e
        }