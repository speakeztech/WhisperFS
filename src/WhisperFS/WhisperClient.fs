namespace WhisperFS

open System
open System.IO
open System.Runtime.InteropServices
open System.Reactive.Linq
open System.Threading
open WhisperFS.Native
open FSharp.Control.Reactive

/// Module for managing cancellation callbacks
module internal CancellationHandler =
    open System.Collections.Concurrent

    // Store active cancellation tokens with their handles and delegates
    let private activeTokens = ConcurrentDictionary<IntPtr, CancellationToken * WhisperAbortCallback>()
    let mutable private nextHandle = 1L

    /// Register a cancellation token and get handle and callback
    let register (token: CancellationToken) =
        let handle = IntPtr(Interlocked.Increment(&nextHandle))

        // Create a delegate for this specific token
        // This needs to be kept alive while the native call is running
        let callback = WhisperAbortCallback(fun userData ->
            if userData = handle then
                token.IsCancellationRequested
            else
                false
        )

        activeTokens.[handle] <- (token, callback)
        let callbackPtr = Marshal.GetFunctionPointerForDelegate(callback)
        (callbackPtr, handle)

    /// Unregister a cancellation token
    let unregister (handle: IntPtr) =
        activeTokens.TryRemove(handle) |> ignore

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

    let getParameters(cancellationToken: CancellationToken option) =
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

        // Strategy-specific parameters (using nested structs)
        match config.Strategy with
        | BeamSearch(beamSize, bestOf) ->
            parameters.beam_search.beam_size <- beamSize
            parameters.greedy.best_of <- bestOf
        | _ ->
            parameters.greedy.best_of <- 1

        // Language and detection
        parameters.language <- languagePtr
        parameters.detect_language <- config.DetectLanguage

        // Suppression
        parameters.suppress_blank <- config.SuppressBlank
        parameters.suppress_nst <- config.SuppressNonSpeech  // Note: field renamed to suppress_nst in new struct

        // Initialize new fields from config
        parameters.debug_mode <- config.DebugMode
        parameters.audio_ctx <- config.AudioContext
        parameters.tdrz_enable <- config.EnableDiarization

        // Handle suppress_regex
        parameters.suppress_regex <-
            match config.SuppressRegex with
            | Some regex ->
                let regexBytes = System.Text.Encoding.UTF8.GetBytes(regex + "\x00")
                let ptr = Marshal.AllocHGlobal(regexBytes.Length)
                Marshal.Copy(regexBytes, 0, ptr, regexBytes.Length)
                ptr
            | None -> IntPtr.Zero

        // Handle initial_prompt
        parameters.initial_prompt <-
            match config.InitialPrompt with
            | Some prompt ->
                let promptBytes = System.Text.Encoding.UTF8.GetBytes(prompt + "\x00")
                let ptr = Marshal.AllocHGlobal(promptBytes.Length)
                Marshal.Copy(promptBytes, 0, ptr, promptBytes.Length)
                ptr
            | None -> IntPtr.Zero
        // Set up cancellation if token provided
        let abortHandle =
            match cancellationToken with
            | Some token ->
                let (callbackPtr, handle) = CancellationHandler.register token
                parameters.abort_callback <- callbackPtr
                parameters.abort_callback_user_data <- handle
                Some handle
            | None ->
                parameters.abort_callback <- IntPtr.Zero
                parameters.abort_callback_user_data <- IntPtr.Zero
                None
        parameters.grammar_rules <- IntPtr.Zero
        parameters.n_grammar_rules <- UIntPtr.Zero
        parameters.i_start_rule <- UIntPtr.Zero
        parameters.grammar_penalty <- 0.0f

        // VAD configuration
        parameters.vad <- config.EnableVAD
        parameters.vad_model_path <-
            match config.VADModelPath with
            | Some path ->
                let pathBytes = System.Text.Encoding.UTF8.GetBytes(path + "\x00")
                let ptr = Marshal.AllocHGlobal(pathBytes.Length)
                Marshal.Copy(pathBytes, 0, ptr, pathBytes.Length)
                ptr
            | None -> IntPtr.Zero

        // Initialize VAD params with sensible defaults
        parameters.vad_params.threshold <- 0.6f
        parameters.vad_params.min_speech_duration_ms <- 250
        parameters.vad_params.min_silence_duration_ms <- 2000
        parameters.vad_params.max_speech_duration_s <- 30.0f
        parameters.vad_params.speech_pad_ms <- 400
        parameters.vad_params.samples_overlap <- 0.5f

        (parameters, abortHandle)

    let freeAllocatedParams (parameters: WhisperFullParams) =
        if parameters.language <> IntPtr.Zero then
            Marshal.FreeHGlobal(parameters.language)
        if parameters.suppress_regex <> IntPtr.Zero then
            Marshal.FreeHGlobal(parameters.suppress_regex)
        if parameters.initial_prompt <> IntPtr.Zero then
            Marshal.FreeHGlobal(parameters.initial_prompt)
        if parameters.vad_model_path <> IntPtr.Zero then
            Marshal.FreeHGlobal(parameters.vad_model_path)

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
                            let (parameters, abortHandle) = getParameters(None)
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
                                freeAllocatedParams parameters
                                match abortHandle with
                                | Some handle -> CancellationHandler.unregister handle
                                | None -> ()
                    })

                stopwatch.Stop()
                return result
            }

        member _.ProcessAsyncWithCancellation(samples, cancellationToken) =
            async {
                let stopwatch = System.Diagnostics.Stopwatch.StartNew()

                let! result = processWithTiming (fun () ->
                    async {
                        if disposed then
                            return Error (StateError "Client is disposed")
                        else
                            let (parameters, abortHandle) = getParameters(Some cancellationToken)
                            try
                                // Process audio
                                let! processResult = WhisperContext.processAudio context parameters samples

                                match processResult with
                                | Ok () ->
                                    // Get segments
                                    match WhisperContext.getSegments context with
                                    | Ok segments ->
                                        let processingTime = stopwatch.Elapsed
                                        let audioLength = TimeSpan.FromSeconds(float samples.Length / 16000.0)
                                        metrics <- { metrics with
                                                        TotalAudioProcessed = metrics.TotalAudioProcessed + audioLength
                                                        SegmentsProcessed = metrics.SegmentsProcessed + segments.Length }

                                        let fullText = segments |> Array.map (fun s -> s.Text) |> String.concat " "
                                        let tokens =
                                            segments
                                            |> Array.collect (fun s -> s.Tokens |> List.toArray)
                                            |> Array.toList

                                        return Ok {
                                            FullText = fullText
                                            Segments = segments |> Array.toList
                                            Duration = audioLength
                                            ProcessingTime = processingTime
                                            Timestamp = DateTime.UtcNow
                                            Language = config.Language
                                            LanguageConfidence = None
                                            Tokens = if tokens.IsEmpty then None else Some tokens
                                        }
                                    | Error e ->
                                        return Error e
                                | Error e ->
                                    return Error e
                            finally
                                freeAllocatedParams parameters
                                match abortHandle with
                                | Some handle -> CancellationHandler.unregister handle
                                | None -> ()
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
                                let (parameters, abortHandle) = getParameters(None)
                                let mutable streamParams = parameters
                                streamParams.single_segment <- true // Process incrementally

                                try
                                    let! processResult = WhisperContext.processAudioWithState streamState streamParams samples

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
                                    freeAllocatedParams streamParams
                                    match abortHandle with
                                    | Some handle -> CancellationHandler.unregister handle
                                    | None -> ()
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

        member _.ProcessFileAsyncWithCancellation(path, cancellationToken) =
            async {
                if disposed then
                    return Error (StateError "Client is disposed")
                else if not (File.Exists(path)) then
                    return Error (FileNotFound path)
                else
                    try
                        // Read audio file and convert to samples
                        // This would need proper audio file reading/decoding
                        // TODO: Use cancellationToken when implementing actual file processing
                        let _ = cancellationToken // Suppress warning - will be used in implementation
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