namespace WhisperFS.Native

open System
open System.Runtime.InteropServices
open System.Threading
open WhisperFS

/// Manages the whisper.cpp context and state
module WhisperContext =

    /// Wrapper for a native whisper context
    type Context =
        { Handle: IntPtr
          ModelPath: string
          IsMultilingual: bool
          mutable IsDisposed: bool }

        interface IDisposable with
            member this.Dispose() =
                if not this.IsDisposed then
                    if this.Handle <> IntPtr.Zero then
                        WhisperNative.whisper_free(this.Handle)
                    this.IsDisposed <- true

    /// Wrapper for a native whisper state (for streaming)
    type State =
        { Handle: IntPtr
          Context: Context
          mutable IsDisposed: bool }

        interface IDisposable with
            member this.Dispose() =
                if not this.IsDisposed then
                    if this.Handle <> IntPtr.Zero then
                        WhisperNative.whisper_free_state(this.Handle)
                    this.IsDisposed <- true

    /// Load a model from file
    let loadModel (modelPath: string) =
        async {
            return
                try
                    let handle = WhisperNative.whisper_init_from_file(modelPath)
                    if handle = IntPtr.Zero then
                        Error (ModelLoadError $"Failed to load model from {modelPath}")
                    else
                        let isMultilingual = WhisperNative.whisper_is_multilingual(handle) <> 0
                        Ok { Handle = handle; ModelPath = modelPath; IsMultilingual = isMultilingual; IsDisposed = false }
                with
                | ex -> Error (NativeLibraryError ex.Message)
        }

    /// Load a model with custom parameters
    let loadModelWithParams (modelPath: string) (parameters: WhisperContextParams) =
        async {
            return
                try
                    let handle = WhisperNative.whisper_init_from_file_with_params(modelPath, parameters)
                    if handle = IntPtr.Zero then
                        Error (ModelLoadError $"Failed to load model from {modelPath}")
                    else
                        let isMultilingual = WhisperNative.whisper_is_multilingual(handle) <> 0
                        Ok { Handle = handle; ModelPath = modelPath; IsMultilingual = isMultilingual; IsDisposed = false }
                with
                | ex -> Error (NativeLibraryError ex.Message)
        }

    /// Create a state for streaming
    let createState (context: Context) =
        if context.IsDisposed then
            Error (StateError "Context is disposed")
        else
            try
                let stateHandle = WhisperNative.whisper_init_state(context.Handle)
                if stateHandle = IntPtr.Zero then
                    Error (NativeLibraryError "Failed to create state")
                else
                    Ok { Handle = stateHandle; Context = context; IsDisposed = false }
            with
            | ex -> Error (NativeLibraryError ex.Message)

    /// Process audio samples
    let processAudio (context: Context) (parameters: WhisperFullParams) (samples: float32[]) =
        async {
            return
                if context.IsDisposed then
                    Error (StateError "Context is disposed")
                else
                    try
                        let result = WhisperNative.whisper_full(context.Handle, parameters, samples, samples.Length)
                        if result = 1 || result = -6 then
                            // whisper.cpp returns 1 for user abort, -6 might be CUDA abort
                            Error WhisperError.OperationCancelled
                        elif result <> 0 then
                            Error (WhisperError.ProcessingError(result, "Processing failed"))
                        else
                            Ok ()
                    with
                    | ex -> Error (NativeLibraryError ex.Message)
        }

    /// Process audio samples with state (for streaming)
    let processAudioWithState (state: State) (parameters: WhisperFullParams) (samples: float32[]) =
        async {
            return
                if state.IsDisposed || state.Context.IsDisposed then
                    Error (StateError "State or context is disposed")
                else
                    try
                        let result = WhisperNative.whisper_full_with_state(
                            state.Context.Handle,
                            state.Handle,
                            parameters,
                            samples,
                            samples.Length)
                        if result = 1 || result = -6 then
                            // whisper.cpp returns 1 for user abort, -6 might be CUDA abort
                            Error WhisperError.OperationCancelled
                        elif result <> 0 then
                            Error (WhisperError.ProcessingError(result, "Processing failed"))
                        else
                            Ok ()
                    with
                    | ex -> Error (NativeLibraryError ex.Message)
        }

    /// Get segments from context
    let getSegments (context: Context) =
        if context.IsDisposed then
            Error (StateError "Context is disposed")
        else
            try
                let segmentCount = WhisperNative.whisper_full_n_segments(context.Handle)
                let segments =
                    [| for i in 0 .. segmentCount - 1 do
                        let textPtr = WhisperNative.whisper_full_get_segment_text(context.Handle, i)
                        let text = Marshal.PtrToStringAnsi(textPtr)
                        let t0 = WhisperNative.whisper_full_get_segment_t0(context.Handle, i)
                        let t1 = WhisperNative.whisper_full_get_segment_t1(context.Handle, i)
                        let speakerTurn = WhisperNative.whisper_full_get_segment_speaker_turn_next(context.Handle, i)

                        // Extract tokens for this segment
                        let tokenCount = WhisperNative.whisper_full_n_tokens(context.Handle, i)
                        let tokens =
                            [| for j in 0 .. tokenCount - 1 do
                                let tokenTextPtr = WhisperNative.whisper_full_get_token_text(context.Handle, i, j)
                                let tokenText = Marshal.PtrToStringAnsi(tokenTextPtr)
                                let tokenId = WhisperNative.whisper_full_get_token_id(context.Handle, i, j)
                                let tokenProb = WhisperNative.whisper_full_get_token_p(context.Handle, i, j)

                                { Text = tokenText
                                  Timestamp = 0.0f  // Token-level timestamps not available from this API
                                  Probability = float32 tokenProb
                                  IsSpecial = tokenId >= 50256 }  // Special tokens typically have high IDs
                            |] |> Array.toList

                        { Text = text
                          StartTime = float32 t0 / 100.0f // Convert from centiseconds
                          EndTime = float32 t1 / 100.0f
                          Tokens = tokens
                          SpeakerTurnNext = speakerTurn
                        }
                    |]
                Ok segments
            with
            | ex -> Error (NativeLibraryError ex.Message)

    /// Get segments from state
    let getSegmentsFromState (state: State) =
        if state.IsDisposed then
            Error (StateError "State is disposed")
        else
            try
                let segmentCount = WhisperNative.whisper_full_n_segments_from_state(state.Handle)
                let segments =
                    [| for i in 0 .. segmentCount - 1 do
                        let textPtr = WhisperNative.whisper_full_get_segment_text_from_state(state.Handle, i)
                        let text = Marshal.PtrToStringAnsi(textPtr)
                        let t0 = WhisperNative.whisper_full_get_segment_t0_from_state(state.Handle, i)
                        let t1 = WhisperNative.whisper_full_get_segment_t1_from_state(state.Handle, i)
                        let speakerTurn = WhisperNative.whisper_full_get_segment_speaker_turn_next_from_state(state.Handle, i)

                        // Extract tokens for this segment
                        let tokenCount = WhisperNative.whisper_full_n_tokens_from_state(state.Handle, i)
                        let tokens =
                            [| for j in 0 .. tokenCount - 1 do
                                let tokenTextPtr = WhisperNative.whisper_full_get_token_text_from_state(state.Handle, i, j)
                                let tokenText = Marshal.PtrToStringAnsi(tokenTextPtr)
                                let tokenId = WhisperNative.whisper_full_get_token_id_from_state(state.Handle, i, j)
                                let tokenProb = WhisperNative.whisper_full_get_token_p_from_state(state.Handle, i, j)

                                { Text = tokenText
                                  Timestamp = 0.0f  // Token-level timestamps not available from this API
                                  Probability = float32 tokenProb
                                  IsSpecial = tokenId >= 50256 }  // Special tokens typically have high IDs
                            |] |> Array.toList

                        { Text = text
                          StartTime = float32 t0 / 100.0f // Convert from centiseconds
                          EndTime = float32 t1 / 100.0f
                          Tokens = tokens
                          SpeakerTurnNext = speakerTurn
                        }
                    |]
                Ok segments
            with
            | ex -> Error (NativeLibraryError ex.Message)

    /// Detect language from audio
    let detectLanguage (context: Context) (samples: float32[]) =
        async {
            return
                if context.IsDisposed then
                    Error (StateError "Context is disposed")
                else if not context.IsMultilingual then
                    Error (ConfigurationError "Model is not multilingual")
                else
                    try
                        // Convert samples to mel spectrogram first
                        let melResult = WhisperNative.whisper_pcm_to_mel(context.Handle, samples, samples.Length, 1)
                        if melResult <> 0 then
                            Error (WhisperError.ProcessingError(melResult, "Failed to convert PCM to mel"))
                        else
                            let maxLangId = WhisperNative.whisper_lang_max_id()
                            let langProbs = Array.zeroCreate<float> (maxLangId + 1)
                            let detectResult = WhisperNative.whisper_lang_auto_detect(context.Handle, 0, 1, langProbs)

                            if detectResult < 0 then
                                Error (WhisperError.ProcessingError(detectResult, "Language detection failed"))
                            else
                                // Find language with highest probability
                                let bestLangId =
                                    langProbs
                                    |> Array.indexed
                                    |> Array.maxBy snd
                                    |> fst

                                let langCodePtr = WhisperNative.whisper_lang_str(bestLangId)
                                let langCode = Marshal.PtrToStringAnsi(langCodePtr)
                                let confidence = float32 langProbs.[bestLangId]

                                Ok { Language = langCode; Confidence = confidence; Probabilities = Map.empty }
                    with
                    | ex -> Error (NativeLibraryError ex.Message)
        }

    /// Get default parameters for a decoding strategy
    let getDefaultParams (strategy: int) =
        WhisperNative.whisper_full_default_params(strategy)

    /// Get model information
    let getModelInfo (context: Context) =
        if context.IsDisposed then
            Error (StateError "Context is disposed")
        else
            try
                let modelTypeInt = WhisperNative.whisper_model_type(context.Handle)
                let modelType =
                    match modelTypeInt with
                    | 0 -> Tiny
                    | 1 -> Base
                    | 2 -> Small
                    | 3 -> Medium
                    | 4 -> LargeV1
                    | 5 -> LargeV2
                    | 6 -> LargeV3
                    | _ -> Tiny // Default

                Ok {
                    Type = modelType
                    VocabSize = WhisperNative.whisper_n_vocab(context.Handle)
                    AudioContext = WhisperNative.whisper_n_audio_ctx(context.Handle)
                    AudioState = WhisperNative.whisper_n_audio_ctx(context.Handle)
                    Languages = if context.IsMultilingual then ["multi"] else ["en"]
                }
            with
            | ex -> Error (NativeLibraryError ex.Message)