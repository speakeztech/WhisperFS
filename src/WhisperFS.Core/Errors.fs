namespace WhisperFS

/// Comprehensive error types for WhisperFS
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

    member this.Message =
        match this with
        | ModelLoadError msg -> sprintf "Failed to load model: %s" msg
        | ProcessingError(code, msg) -> sprintf "Processing error %d: %s" code msg
        | InvalidAudioFormat msg -> sprintf "Invalid audio format: %s" msg
        | StateError msg -> sprintf "State error: %s" msg
        | NativeLibraryError msg -> sprintf "Native library error: %s" msg
        | TokenizationError msg -> sprintf "Tokenization error: %s" msg
        | ConfigurationError msg -> sprintf "Configuration error: %s" msg
        | FileNotFound path -> sprintf "File not found: %s" path
        | NetworkError msg -> sprintf "Network error: %s" msg
        | OutOfMemory -> "Out of memory"
        | Cancelled -> "Operation cancelled"
        | NotImplemented feature -> sprintf "Feature not implemented: %s" feature

module Result =
    /// Map error type
    let mapError f result =
        match result with
        | Ok value -> Ok value
        | Error err -> Error (f err)

    /// Bind with error handling
    let bindWithError f result =
        match result with
        | Ok value -> f value
        | Error err -> Error err

    /// Try to execute function and convert exception to error
    let tryWith f arg =
        try
            Ok (f arg)
        with
        | :? System.OutOfMemoryException -> Error OutOfMemory
        | :? System.OperationCanceledException -> Error Cancelled
        | :? System.IO.FileNotFoundException as ex -> Error (FileNotFound ex.FileName)
        | ex -> Error (ProcessingError(0, ex.Message))

    /// Execute async function with error handling
    let tryAsync f =
        async {
            try
                let! result = f()
                return Ok result
            with
            | :? System.OutOfMemoryException -> return Error OutOfMemory
            | :? System.OperationCanceledException -> return Error Cancelled
            | :? System.IO.FileNotFoundException as ex -> return Error (FileNotFound ex.FileName)
            | ex -> return Error (ProcessingError(0, ex.Message))
        }

module ErrorHandling =
    open System
    open System.Threading.Tasks

    /// Convert native error codes to discriminated unions
    let mapNativeError (code: int) =
        match code with
        | -1 -> ModelLoadError "Failed to load model"
        | -2 -> InvalidAudioFormat "Invalid audio format"
        | -3 -> OutOfMemory
        | -4 -> StateError "Invalid state"
        | -5 -> TokenizationError "Failed to tokenize"
        | _ -> ProcessingError(code, sprintf "Unknown error code: %d" code)

    /// Retry logic with exponential backoff
    let retryWithBackoff<'T> (operation: unit -> Async<Result<'T, WhisperError>>) (maxRetries: int) =
        let rec retry attempt delay =
            async {
                match! operation() with
                | Ok result -> return Ok result
                | Error err when attempt < maxRetries ->
                    match err with
                    | OutOfMemory | ProcessingError _ ->
                        // Retryable errors
                        do! Async.Sleep (int delay)
                        return! retry (attempt + 1) (delay * 2)
                    | _ ->
                        // Non-retryable errors
                        return Error err
                | Error err -> return Error err
            }
        retry 0 100

    /// Resource cleanup with error handling
    let useResource acquire release action =
        async {
            let resource = acquire()
            try
                return! action resource
            finally
                try release resource
                with _ -> () // Suppress cleanup errors
        }

    /// Validate configuration
    let validateConfig (config: WhisperConfig) =
        [
            if config.ThreadCount <= 0 then
                yield ConfigurationError "Thread count must be positive"

            if config.ChunkSizeMs <= 0 then
                yield ConfigurationError "Chunk size must be positive"

            if config.OverlapMs < 0 then
                yield ConfigurationError "Overlap cannot be negative"

            if config.OverlapMs >= config.ChunkSizeMs then
                yield ConfigurationError "Overlap must be less than chunk size"

            if config.MinConfidence < 0.0f || config.MinConfidence > 1.0f then
                yield ConfigurationError "Min confidence must be between 0 and 1"

            if config.Temperature < 0.0f then
                yield ConfigurationError "Temperature must be non-negative"

            if config.MaxContext <= 0 then
                yield ConfigurationError "Max context must be positive"
        ]
        |> function
            | [] -> Ok config
            | err::_ -> Error err