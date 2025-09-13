namespace WhisperFS

open System
open System.Runtime.InteropServices
open System.Reactive.Linq
open System.Reactive.Subjects
open FSharp.Control.Reactive
open WhisperFS.Native

/// Streaming implementation using whisper_state
type WhisperStream(ctx: IntPtr, state: IntPtr, config: WhisperConfig) =
    let events = new Subject<TranscriptionEvent>()
    let mutable previousSegmentCount = 0
    let mutable committedText = ""
    let pendingAudio = ResizeArray<float32>()
    let mutable isDisposed = false

    // Callback handlers for streaming
    let mutable newSegmentCallback = Unchecked.defaultof<WhisperNewSegmentCallback>
    let mutable encoderBeginCallback = Unchecked.defaultof<WhisperEncoderBeginCallback>
    let mutable progressCallback = Unchecked.defaultof<WhisperProgressCallback>

    // GC handles for callbacks
    let mutable newSegmentHandle = Unchecked.defaultof<GCHandle>
    let mutable encoderHandle = Unchecked.defaultof<GCHandle>
    let mutable progressHandle = Unchecked.defaultof<GCHandle>

    // Setup callbacks
    let setupCallbacks() =
        newSegmentCallback <- WhisperNewSegmentCallback(fun ctx state n_new userData ->
            // Process new segments as they arrive
            let totalSegments = WhisperNative.whisper_full_n_segments_from_state(state)

            for i in previousSegmentCount .. totalSegments - 1 do
                let textPtr = WhisperNative.whisper_full_get_segment_text_from_state(state, i)
                let text = Marshal.PtrToStringAnsi(textPtr)
                let t0 = WhisperNative.whisper_full_get_segment_t0_from_state(state, i)
                let t1 = WhisperNative.whisper_full_get_segment_t1_from_state(state, i)

                // Get tokens with confidence
                let tokenCount = WhisperNative.whisper_full_n_tokens_from_state(state, i)
                let tokens = [
                    for j in 0 .. tokenCount - 1 do
                        let tokenPtr = WhisperNative.whisper_full_get_token_text_from_state(state, i, j)
                        let tokenText = Marshal.PtrToStringAnsi(tokenPtr)
                        let prob = WhisperNative.whisper_full_get_token_p_from_state(state, i, j)
                        yield {
                            Text = tokenText
                            Timestamp = float32 t0 / 1000.0f  // Convert ms to seconds
                            Probability = prob
                            IsSpecial = tokenText.StartsWith("<|") && tokenText.EndsWith("|>")
                        }
                ]

                let segment = {
                    Text = text
                    StartTime = float32 t0 / 1000.0f  // Convert ms to seconds
                    EndTime = float32 t1 / 1000.0f    // Convert ms to seconds
                    Tokens = tokens
                }

                // Calculate confidence
                let confidence =
                    tokens
                    |> List.filter (fun t -> not t.IsSpecial)
                    |> function
                        | [] -> 0.0f
                        | tokens -> tokens |> List.averageBy (fun t -> t.Probability)

                // Emit event
                events.OnNext(PartialTranscription(text, tokens, confidence))

            previousSegmentCount <- totalSegments
        )

        encoderBeginCallback <- WhisperEncoderBeginCallback(fun ctx state userData ->
            // Return true to proceed with encoding
            true
        )

        progressCallback <- WhisperProgressCallback(fun ctx state progress userData ->
            // Could emit progress events if needed
            ()
        )

        // Pin callbacks for P/Invoke
        newSegmentHandle <- GCHandle.Alloc(newSegmentCallback)
        encoderHandle <- GCHandle.Alloc(encoderBeginCallback)
        progressHandle <- GCHandle.Alloc(progressCallback)

    do setupCallbacks()

    /// Process chunk incrementally using whisper_state
    let processChunkInternal (samples: float32[]) = async {
        try
            // Append new audio to pending buffer
            pendingAudio.AddRange(samples)

            // Only process if we have enough audio (e.g., 1 second)
            if pendingAudio.Count >= config.ChunkSizeMs * 16 then
                let audioToProcess = pendingAudio.ToArray()

                // Prepare parameters for streaming
                let mutable parameters = WhisperNative.whisper_full_default_params(
                    match config.SamplingStrategy with
                    | Greedy -> WhisperSamplingStrategy.WHISPER_SAMPLING_GREEDY
                    | BeamSearch(size, best) -> WhisperSamplingStrategy.WHISPER_SAMPLING_BEAM_SEARCH
                )

                parameters.n_threads <- config.ThreadCount
                parameters.offset_ms <- 0  // Process from start of chunk
                parameters.duration_ms <- 0  // Process entire chunk
                parameters.translate <- config.EnableTranslate
                parameters.no_context <- config.NoContext
                parameters.single_segment <- config.SingleSegment
                parameters.token_timestamps <- config.TokenTimestamps
                parameters.suppress_blank <- config.SuppressBlank
                parameters.suppress_non_speech_tokens <- config.SuppressNonSpeechTokens
                parameters.temperature <- config.Temperature
                parameters.temperature_inc <- config.TemperatureInc
                parameters.entropy_thold <- config.EntropyThreshold
                parameters.logprob_thold <- config.LogProbThreshold
                parameters.no_speech_thold <- config.NoSpeechThreshold
                parameters.max_initial_ts <- config.MaxInitialTs
                parameters.length_penalty <- config.LengthPenalty
                parameters.max_len <- config.MaxLen
                parameters.split_on_word <- config.SplitOnWord
                parameters.max_tokens <- config.MaxTokens
                parameters.thold_pt <- config.ThresholdPt
                parameters.thold_ptsum <- config.ThresholdPtSum

                // Set beam search parameters if applicable
                match config.SamplingStrategy with
                | BeamSearch(size, best) ->
                    parameters.beam_size <- size
                    parameters.best_of <- best
                | _ -> ()

                // Set language
                match config.Language with
                | Some lang ->
                    parameters.language <- Marshal.StringToHGlobalAnsi(lang)
                    parameters.detect_language <- false
                | None ->
                    parameters.detect_language <- true

                // Set prompt tokens if provided
                let mutable promptTokenPtr = IntPtr.Zero
                match config.InitialPrompt with
                | Some prompt ->
                    let maxTokens = 512
                    let tokens = Array.zeroCreate<int> maxTokens
                    let tokenCount = WhisperNative.whisper_tokenize(ctx, prompt, tokens, maxTokens)
                    if tokenCount > 0 then
                        promptTokenPtr <- Marshal.AllocHGlobal(tokenCount * sizeof<int>)
                        Marshal.Copy(tokens, 0, promptTokenPtr, tokenCount)
                        parameters.prompt_tokens <- promptTokenPtr
                        parameters.prompt_n_tokens <- tokenCount
                | None -> ()

                // Set callbacks
                parameters.new_segment_callback <- Marshal.GetFunctionPointerForDelegate(newSegmentCallback)
                parameters.encoder_begin_callback <- Marshal.GetFunctionPointerForDelegate(encoderBeginCallback)
                parameters.progress_callback <- Marshal.GetFunctionPointerForDelegate(progressCallback)

                try
                    // Process with state for incremental transcription
                    let result = WhisperNative.whisper_full_with_state(
                        ctx,
                        state,  // Use state for continuity
                        parameters,
                        audioToProcess,
                        audioToProcess.Length)

                    // Free prompt tokens if allocated
                    if promptTokenPtr <> IntPtr.Zero then
                        Marshal.FreeHGlobal(promptTokenPtr)

                    // Free language string if allocated
                    match config.Language with
                    | Some _ when parameters.language <> IntPtr.Zero ->
                        Marshal.FreeHGlobal(parameters.language)
                    | _ -> ()

                    // Clear processed audio
                    pendingAudio.Clear()

                    if result = 0 then
                        // Get latest results from state
                        let segmentCount = WhisperNative.whisper_full_n_segments_from_state(state)
                        if segmentCount > 0 then
                            let segments = [
                                for i in 0 .. segmentCount - 1 do
                                    let textPtr = WhisperNative.whisper_full_get_segment_text_from_state(state, i)
                                    let text = Marshal.PtrToStringAnsi(textPtr)
                                    let t0 = WhisperNative.whisper_full_get_segment_t0_from_state(state, i)
                                    let t1 = WhisperNative.whisper_full_get_segment_t1_from_state(state, i)
                                    yield {
                                        Text = text
                                        StartTime = float32 t0 / 1000.0f
                                        EndTime = float32 t1 / 1000.0f
                                        Tokens = []
                                    }
                            ]

                            // Build complete transcription
                            let fullText = segments |> List.map (fun s -> s.Text) |> String.concat " "
                            committedText <- fullText

                            return FinalTranscription(fullText, [], segments)
                        else
                            return PartialTranscription("", [], 0.0f)
                    else
                        return ProcessingError(sprintf "Whisper processing failed with code %d" result)

                with ex ->
                    // Clean up on error
                    if promptTokenPtr <> IntPtr.Zero then
                        Marshal.FreeHGlobal(promptTokenPtr)
                    match config.Language with
                    | Some _ when parameters.language <> IntPtr.Zero ->
                        Marshal.FreeHGlobal(parameters.language)
                    | _ -> ()
                    return ProcessingError(ex.Message)
            else
                // Not enough audio yet, just acknowledge
                return PartialTranscription("", [], 0.0f)

        with ex ->
            return ProcessingError(ex.Message)
    }

    /// Process a chunk of audio samples
    member _.ProcessChunk(samples: float32[]) =
        processChunkInternal samples

    /// Reset the stream state
    member _.Reset() =
        WhisperNative.whisper_free_state(state)
        let newState = WhisperNative.whisper_init_state(ctx)
        previousSegmentCount <- 0
        committedText <- ""
        pendingAudio.Clear()

    /// Get events observable
    member _.Events = events.AsObservable()

    /// Get committed text
    member _.CommittedText = committedText

    /// Process audio stream
    member _.ProcessStream(audioStream: IObservable<float32[]>) =
        audioStream
        |> Observable.selectAsync (fun samples ->
            async {
                let! result = processChunkInternal samples
                return result
            })

    interface IDisposable with
        member _.Dispose() =
            if not isDisposed then
                isDisposed <- true

                // Free callbacks
                if newSegmentHandle.IsAllocated then newSegmentHandle.Free()
                if encoderHandle.IsAllocated then encoderHandle.Free()
                if progressHandle.IsAllocated then progressHandle.Free()

                // Complete events
                events.OnCompleted()
                events.Dispose()

                // Free native resources
                WhisperNative.whisper_free_state(state)

module StreamProcessing =
    open System
    open System.Reactive.Linq
    open FSharp.Control.Reactive

    /// Create a streaming transcription pipeline
    let createTranscriptionPipeline (audioSource: IObservable<float32[]>) (stream: WhisperStream) =
        audioSource
        // Buffer audio into chunks
        |> Observable.bufferTimeSpan (TimeSpan.FromMilliseconds(float stream.ChunkSizeMs))
        |> Observable.map (Array.concat)

        // Process through whisper
        |> Observable.selectAsync (fun chunk -> stream.ProcessChunk(chunk))

        // Filter based on confidence
        |> Observable.choose (function
            | PartialTranscription(text, _, conf) when conf >= stream.MinConfidence ->
                Some text
            | _ -> None)

        // Debounce rapid changes
        |> Observable.throttle (TimeSpan.FromMilliseconds 200.0)

    /// Smart text stabilization
    let stabilizeText (events: IObservable<TranscriptionEvent>) =
        events
        |> Observable.scan (fun (lastText, lastConf) event ->
            match event with
            | PartialTranscription(text, _, conf) ->
                // Only update if more confident or extending
                if conf > lastConf || text.StartsWith(lastText) then
                    (text, conf)
                else
                    (lastText, lastConf)
            | FinalTranscription(text, _, _) ->
                (text, 1.0f) // Final is always accepted
            | _ -> (lastText, lastConf)
        ) ("", 0.0f)
        |> Observable.map fst
        |> Observable.distinctUntilChanged

    /// Incremental typing with correction support
    type TypedTextState = {
        Committed: string  // Text already typed
        Pending: string     // Text waiting to be typed
        LastUpdate: DateTime
    }

    type TypingCommand =
        | TypeText of string
        | Backspace of count:int
        | Clear

    let createTypingPipeline (transcriptions: IObservable<string>) =
        transcriptions
        |> Observable.scan (fun state text ->
            // Check if we need to correct
            if text.StartsWith(state.Committed) then
                // Extension - just add pending
                { state with
                    Pending = text.Substring(state.Committed.Length)
                    LastUpdate = DateTime.UtcNow }
            else
                // Correction needed - calculate common prefix
                let commonPrefix =
                    Seq.zip state.Committed text
                    |> Seq.takeWhile (fun (a, b) -> a = b)
                    |> Seq.length

                { Committed = text.Substring(0, commonPrefix)
                  Pending = text.Substring(commonPrefix)
                  LastUpdate = DateTime.UtcNow }
        ) { Committed = ""; Pending = ""; LastUpdate = DateTime.UtcNow }

        // Emit typing commands
        |> Observable.map (fun state ->
            if state.Pending.Length > 0 then
                Some (TypeText state.Pending)
            else
                None)
        |> Observable.choose id