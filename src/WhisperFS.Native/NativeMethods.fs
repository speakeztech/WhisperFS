namespace WhisperFS.Native

open System
open System.Runtime.InteropServices

module WhisperNative =

    [<Literal>]
    let LibraryName = "whisper"

    // Context initialization
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_init_from_file(string path)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_init_from_file_with_params(string path, WhisperContextParams parameters)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_init_from_buffer(IntPtr buffer, UIntPtr buffer_size)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_init_from_buffer_with_params(IntPtr buffer, UIntPtr buffer_size, WhisperContextParams parameters)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void whisper_free(IntPtr ctx)

    // State management - CRITICAL for streaming
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_init_state(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void whisper_free_state(IntPtr state)

    // Core processing functions
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full(
        IntPtr ctx,
        WhisperFullParams parameters,
        [<In>] float32[] samples,
        int n_samples)

    // Streaming-specific processing with state
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_with_state(
        IntPtr ctx,
        IntPtr state,
        WhisperFullParams parameters,
        [<In>] float32[] samples,
        int n_samples)

    // Parallel processing
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_parallel(
        IntPtr ctx,
        WhisperFullParams parameters,
        [<In>] float32[] samples,
        int n_samples,
        int n_processors)

    // Segment access from context
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_n_segments(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_full_get_segment_text(IntPtr ctx, int i_segment)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t0(IntPtr ctx, int i_segment)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t1(IntPtr ctx, int i_segment)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool whisper_full_get_segment_speaker_turn_next(IntPtr ctx, int i_segment)

    // Segment access from state (for streaming)
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_n_segments_from_state(IntPtr state)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_full_get_segment_text_from_state(IntPtr state, int i_segment)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t0_from_state(IntPtr state, int i_segment)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t1_from_state(IntPtr state, int i_segment)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool whisper_full_get_segment_speaker_turn_next_from_state(IntPtr state, int i_segment)

    // Token access from context
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_n_tokens(IntPtr ctx, int i_segment)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_full_get_token_text(IntPtr ctx, int i_segment, int i_token)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_get_token_id(IntPtr ctx, int i_segment, int i_token)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern WhisperTokenData whisper_full_get_token_data(IntPtr ctx, int i_segment, int i_token)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern float whisper_full_get_token_p(IntPtr ctx, int i_segment, int i_token)

    // Token access from state
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_n_tokens_from_state(IntPtr state, int i_segment)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_full_get_token_text_from_state(IntPtr state, int i_segment, int i_token)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_full_get_token_id_from_state(IntPtr state, int i_segment, int i_token)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern WhisperTokenData whisper_full_get_token_data_from_state(IntPtr state, int i_segment, int i_token)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern float whisper_full_get_token_p_from_state(IntPtr state, int i_segment, int i_token)

    // Tokenization for prompts
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_tokenize(
        IntPtr ctx,
        [<MarshalAs(UnmanagedType.LPStr)>] string text,
        [<Out>] int[] tokens,
        int n_max_tokens)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_token_count(IntPtr ctx, [<MarshalAs(UnmanagedType.LPStr)>] string text)

    // Language functions
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_lang_max_id()

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_lang_id([<MarshalAs(UnmanagedType.LPStr)>] string lang)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_lang_str(int id)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern IntPtr whisper_lang_str_full(int id)

    // Language detection
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_lang_auto_detect(
        IntPtr ctx,
        int offset_ms,
        int n_threads,
        [<Out>] float[] lang_probs)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_lang_auto_detect_with_state(
        IntPtr ctx,
        IntPtr state,
        int offset_ms,
        int n_threads,
        [<Out>] float[] lang_probs)

    // Model info
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_n_len(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_n_len_from_state(IntPtr state)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_n_vocab(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_n_text_ctx(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_n_audio_ctx(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_is_multilingual(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_vocab(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_audio_ctx(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_audio_state(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_audio_head(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_audio_layer(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_text_ctx(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_text_state(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_text_head(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_text_layer(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_n_mels(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_ftype(IntPtr ctx)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_model_type(IntPtr ctx)

    // Timing
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t0_from_state(IntPtr state, int i_segment)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int64 whisper_full_get_segment_t1_from_state(IntPtr state, int i_segment)

    // Reset timings
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void whisper_reset_timings(IntPtr ctx)

    // Print timings
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void whisper_print_timings(IntPtr ctx)

    // Context parameters
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern WhisperContextParams whisper_context_default_params()

    // Full parameters
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern WhisperFullParams whisper_full_default_params(int strategy)

    // PCM to Mel
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_pcm_to_mel(
        IntPtr ctx,
        [<In>] float32[] samples,
        int n_samples,
        int n_threads)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_pcm_to_mel_with_state(
        IntPtr ctx,
        IntPtr state,
        [<In>] float32[] samples,
        int n_samples,
        int n_threads)

    // Set Mel
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_set_mel(
        IntPtr ctx,
        [<In>] float32[] data,
        int n_len,
        int n_mel)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_set_mel_with_state(
        IntPtr ctx,
        IntPtr state,
        [<In>] float32[] data,
        int n_len,
        int n_mel)

    // Encode
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_encode(IntPtr ctx, int offset, int n_threads)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_encode_with_state(IntPtr ctx, IntPtr state, int offset, int n_threads)

    // Decode
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_decode(IntPtr ctx, IntPtr tokens, int n_tokens, int n_past, int n_threads)

    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int whisper_decode_with_state(
        IntPtr ctx,
        IntPtr state,
        IntPtr tokens,
        int n_tokens,
        int n_past,
        int n_threads)