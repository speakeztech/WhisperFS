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

/// Full parameters structure - must match C struct exactly
[<Struct; StructLayout(LayoutKind.Sequential)>]
type WhisperFullParams =
    val mutable strategy: int                    // Sampling strategy (0=GREEDY, 1=BEAM_SEARCH)
    val mutable n_threads: int                   // Number of threads
    val mutable n_max_text_ctx: int             // Max tokens to use from past text as prompt
    val mutable offset_ms: int                   // Start offset in ms
    val mutable duration_ms: int                 // Audio duration to process in ms
    val mutable translate: bool                  // Translate to English
    val mutable no_context: bool                 // Do not use past transcription for context
    val mutable no_timestamps: bool              // Do not generate timestamps
    val mutable single_segment: bool             // Force single segment output
    val mutable print_special: bool              // Print special tokens
    val mutable print_progress: bool             // Print progress info
    val mutable print_realtime: bool             // Print results from within whisper.cpp
    val mutable print_timestamps: bool           // Print timestamps for each text segment
    val mutable token_timestamps: bool           // Enable token-level timestamps
    val mutable thold_pt: float32               // Timestamp token probability threshold
    val mutable thold_ptsum: float32            // Timestamp token sum probability threshold
    val mutable max_len: int                     // Max segment length in characters
    val mutable split_on_word: bool             // Split on word rather than token
    val mutable max_tokens: int                  // Max tokens per segment (0=no limit)

    // Temperature sampling parameters
    val mutable temperature: float32             // Initial temperature
    val mutable temperature_inc: float32         // Temperature increment for fallbacks
    val mutable entropy_thold: float32          // Entropy threshold for decoder fallback
    val mutable logprob_thold: float32          // Log probability threshold for decoder fallback
    val mutable no_speech_thold: float32        // No-speech probability threshold

    // Beam search parameters (when strategy = BEAM_SEARCH)
    val mutable beam_size: int                   // Beam size for beam search
    val mutable best_of: int                     // Number of best candidates to keep
    val mutable patience: float32                // Patience for beam search

    // Prompt tokens (must be allocated and tokenized)
    val mutable prompt_tokens: IntPtr            // Pointer to prompt token array
    val mutable prompt_n_tokens: int             // Number of prompt tokens

    // Language
    val mutable language: IntPtr                 // Language hint ("en", "de", etc.)
    val mutable detect_language: bool            // Auto-detect language

    // Suppression
    val mutable suppress_blank: bool             // Suppress blank outputs
    val mutable suppress_non_speech_tokens: bool // Suppress non-speech tokens

    // Initial timestamp
    val mutable max_initial_ts: float32          // Max initial timestamp
    val mutable length_penalty: float32          // Length penalty

    // Callbacks for streaming
    val mutable new_segment_callback: IntPtr     // Callback for new segments
    val mutable new_segment_callback_user_data: IntPtr
    val mutable encoder_begin_callback: IntPtr   // Callback before encoding
    val mutable encoder_begin_callback_user_data: IntPtr
    val mutable logits_filter_callback: IntPtr   // Callback for filtering logits
    val mutable logits_filter_callback_user_data: IntPtr
    val mutable progress_callback: IntPtr        // Progress callback
    val mutable progress_callback_user_data: IntPtr

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