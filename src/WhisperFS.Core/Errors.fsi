namespace WhisperFS

/// Core error types for WhisperFS operations
type WhisperError =
    | ModelLoadError of message:string
    | ProcessingError of code:int * message:string
    | InvalidAudioFormat of message:string
    | StateError of message:string
    | NativeLibraryError of message:string
    | TokenizationError of message:string
    | ConfigurationError of message:string
    | FileNotFound of path:string
    | NetworkError of message:string
    | OutOfMemory
    | Cancelled
    | NotImplemented of feature:string

    /// Get human-readable error message
    member Message: string