namespace WhisperFS.Native

open System
open System.Runtime.InteropServices

/// Native callback delegates for streaming
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type WhisperNewSegmentCallback =
    delegate of ctx:IntPtr * state:IntPtr * n_new:int * user_data:IntPtr -> unit

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type WhisperEncoderBeginCallback =
    delegate of ctx:IntPtr * state:IntPtr * user_data:IntPtr -> bool

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type WhisperLogitsFilterCallback =
    delegate of ctx:IntPtr * state:IntPtr * tokens:IntPtr * n_tokens:int * logits:IntPtr * user_data:IntPtr -> unit

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type WhisperProgressCallback =
    delegate of ctx:IntPtr * state:IntPtr * progress:int * user_data:IntPtr -> unit

/// Abort callback for cancellation support
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type WhisperAbortCallback =
    delegate of user_data:IntPtr -> bool  // Returns true to abort

/// Nested struct for greedy parameters
[<Struct; StructLayout(LayoutKind.Sequential)>]
type WhisperGreedyParams =
    val mutable best_of: int

/// Nested struct for beam search parameters
[<Struct; StructLayout(LayoutKind.Sequential)>]
type WhisperBeamSearchParams =
    val mutable beam_size: int
    val mutable patience: float32

/// VAD parameters structure - must match whisper_vad_params exactly
[<Struct; StructLayout(LayoutKind.Sequential)>]
type WhisperVadParams =
    val mutable threshold: float32                 // Probability threshold to consider as speech
    val mutable min_speech_duration_ms: int        // Min duration for a valid speech segment
    val mutable min_silence_duration_ms: int       // Min silence duration to consider speech as ended
    val mutable max_speech_duration_s: float32     // Max duration of a speech segment before forcing a new segment
    val mutable speech_pad_ms: int                 // Padding added before and after speech segments
    val mutable samples_overlap: float32           // Overlap in seconds when copying audio samples from speech segment

/// Full parameters structure - must match C struct exactly
[<Struct; StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)>]
type WhisperFullParams =
    val mutable strategy: int                    // Sampling strategy (0=GREEDY, 1=BEAM_SEARCH)
    val mutable n_threads: int                   // Number of threads
    val mutable n_max_text_ctx: int             // Max tokens to use from past text as prompt
    val mutable offset_ms: int                   // Start offset in ms
    val mutable duration_ms: int                 // Audio duration to process in ms
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable translate: bool                  // Translate to English
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable no_context: bool                 // Do not use past transcription for context
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable no_timestamps: bool              // Do not generate timestamps
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable single_segment: bool             // Force single segment output
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable print_special: bool              // Print special tokens
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable print_progress: bool             // Print progress info
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable print_realtime: bool             // Print results from within whisper.cpp
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable print_timestamps: bool           // Print timestamps for each text segment

    // Token-level timestamps
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable token_timestamps: bool           // Enable token-level timestamps
    val mutable thold_pt: float32               // Timestamp token probability threshold
    val mutable thold_ptsum: float32            // Timestamp token sum probability threshold
    val mutable max_len: int                     // Max segment length in characters
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable split_on_word: bool             // Split on word rather than token
    val mutable max_tokens: int                  // Max tokens per segment (0=no limit)

    // Speed-up techniques
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable debug_mode: bool                 // Enable debug mode
    val mutable audio_ctx: int                   // Overwrite audio context size

    // Tinydiarize
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable tdrz_enable: bool                // Enable tinydiarize

    // Suppression regex
    val mutable suppress_regex: IntPtr           // Regular expression for token suppression

    // Initial prompt
    val mutable initial_prompt: IntPtr           // Initial prompt text
    val mutable prompt_tokens: IntPtr            // Pointer to prompt token array
    val mutable prompt_n_tokens: int             // Number of prompt tokens

    // Language
    val mutable language: IntPtr                 // Language hint ("en", "de", etc.)
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable detect_language: bool            // Auto-detect language

    // Common decoding parameters
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable suppress_blank: bool             // Suppress blank outputs
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable suppress_nst: bool               // Suppress non-speech tokens

    val mutable temperature: float32             // Initial temperature
    val mutable max_initial_ts: float32          // Max initial timestamp
    val mutable length_penalty: float32          // Length penalty

    // Fallback parameters
    val mutable temperature_inc: float32         // Temperature increment for fallbacks
    val mutable entropy_thold: float32          // Entropy threshold for decoder fallback
    val mutable logprob_thold: float32          // Log probability threshold for decoder fallback
    val mutable no_speech_thold: float32        // No-speech probability threshold

    // Greedy parameters (nested struct)
    val mutable greedy: WhisperGreedyParams

    // Beam search parameters (nested struct)
    val mutable beam_search: WhisperBeamSearchParams

    // Callbacks for streaming
    val mutable new_segment_callback: IntPtr     // Callback for new segments
    val mutable new_segment_callback_user_data: IntPtr
    val mutable progress_callback: IntPtr        // Progress callback
    val mutable progress_callback_user_data: IntPtr
    val mutable encoder_begin_callback: IntPtr   // Callback before encoding
    val mutable encoder_begin_callback_user_data: IntPtr
    val mutable abort_callback: IntPtr           // Abort callback
    val mutable abort_callback_user_data: IntPtr
    val mutable logits_filter_callback: IntPtr   // Callback for filtering logits
    val mutable logits_filter_callback_user_data: IntPtr

    // Grammar rules
    val mutable grammar_rules: IntPtr            // Grammar rules
    val mutable n_grammar_rules: UIntPtr         // Number of grammar rules
    val mutable i_start_rule: UIntPtr            // Start rule index
    val mutable grammar_penalty: float32         // Grammar penalty

    // VAD parameters
    [<MarshalAs(UnmanagedType.I1)>]
    val mutable vad: bool                        // Enable VAD
    val mutable vad_model_path: IntPtr           // Path to VAD model
    val mutable vad_params: WhisperVadParams     // VAD parameters

/// Token data structure
[<Struct; StructLayout(LayoutKind.Sequential)>]
type WhisperTokenData =
    val mutable id: int
    val mutable tid: int
    val mutable p: float32
    val mutable plog: float32
    val mutable pt: float32
    val mutable ptsum: float32
    val mutable t0: int64
    val mutable t1: int64
    val mutable vlen: float32

/// Context parameters for initialization
[<Struct; StructLayout(LayoutKind.Sequential)>]
type WhisperContextParams =
    val mutable use_gpu: bool
    val mutable gpu_device: int
    val mutable flash_attn: bool
    val mutable dtw_token_timestamps: bool
    val mutable dtw_aheads_preset: int
    val mutable dtw_n_top: int
    val mutable dtw_aheads: IntPtr
    val mutable dtw_mem_size: UIntPtr

/// Sampling strategy constants
module WhisperSamplingStrategy =
    let [<Literal>] WHISPER_SAMPLING_GREEDY = 0
    let [<Literal>] WHISPER_SAMPLING_BEAM_SEARCH = 1

/// Language codes
module WhisperLanguage =
    let languages = [
        "en", "English"
        "zh", "Chinese"
        "de", "German"
        "es", "Spanish"
        "ru", "Russian"
        "ko", "Korean"
        "fr", "French"
        "ja", "Japanese"
        "pt", "Portuguese"
        "tr", "Turkish"
        "pl", "Polish"
        "ca", "Catalan"
        "nl", "Dutch"
        "ar", "Arabic"
        "sv", "Swedish"
        "it", "Italian"
        "id", "Indonesian"
        "hi", "Hindi"
        "fi", "Finnish"
        "vi", "Vietnamese"
        "he", "Hebrew"
        "uk", "Ukrainian"
        "el", "Greek"
        "ms", "Malay"
        "cs", "Czech"
        "ro", "Romanian"
        "da", "Danish"
        "hu", "Hungarian"
        "ta", "Tamil"
        "no", "Norwegian"
        "th", "Thai"
        "ur", "Urdu"
        "hr", "Croatian"
        "bg", "Bulgarian"
        "lt", "Lithuanian"
        "la", "Latin"
        "mi", "Maori"
        "ml", "Malayalam"
        "cy", "Welsh"
        "sk", "Slovak"
        "te", "Telugu"
        "fa", "Persian"
        "lv", "Latvian"
        "bn", "Bengali"
        "sr", "Serbian"
        "az", "Azerbaijani"
        "sl", "Slovenian"
        "kn", "Kannada"
        "et", "Estonian"
        "mk", "Macedonian"
        "br", "Breton"
        "eu", "Basque"
        "is", "Icelandic"
        "hy", "Armenian"
        "ne", "Nepali"
        "mn", "Mongolian"
        "bs", "Bosnian"
        "kk", "Kazakh"
        "sq", "Albanian"
        "sw", "Swahili"
        "gl", "Galician"
        "mr", "Marathi"
        "pa", "Punjabi"
        "si", "Sinhala"
        "km", "Khmer"
        "sn", "Shona"
        "yo", "Yoruba"
        "so", "Somali"
        "af", "Afrikaans"
        "oc", "Occitan"
        "ka", "Georgian"
        "be", "Belarusian"
        "tg", "Tajik"
        "sd", "Sindhi"
        "gu", "Gujarati"
        "am", "Amharic"
        "yi", "Yiddish"
        "lo", "Lao"
        "uz", "Uzbek"
        "fo", "Faroese"
        "ht", "Haitian Creole"
        "ps", "Pashto"
        "tk", "Turkmen"
        "nn", "Nynorsk"
        "mt", "Maltese"
        "sa", "Sanskrit"
        "lb", "Luxembourgish"
        "my", "Myanmar"
        "bo", "Tibetan"
        "tl", "Tagalog"
        "mg", "Malagasy"
        "as", "Assamese"
        "tt", "Tatar"
        "haw", "Hawaiian"
        "ln", "Lingala"
        "ha", "Hausa"
        "ba", "Bashkir"
        "jw", "Javanese"
        "su", "Sundanese"
        "yue", "Cantonese"
    ]

    let getLanguageCode index =
        if index >= 0 && index < languages.Length then
            fst languages.[index]
        else
            "unknown"

    let getLanguageName code =
        languages
        |> List.tryFind (fun (c, _) -> c = code)
        |> Option.map snd
        |> Option.defaultValue "Unknown"