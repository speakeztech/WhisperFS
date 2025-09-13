namespace WhisperFS

/// Main WhisperFS API - Simplified interface matching current implementation
module WhisperFS =

    /// Initialize WhisperFS native libraries (call once at startup)
    val initialize: unit -> Async<Result<unit, WhisperError>>

    /// Create a whisper client with configuration
    val createClient: config:WhisperConfig -> IWhisperClient