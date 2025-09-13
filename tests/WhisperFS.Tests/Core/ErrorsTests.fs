module WhisperFS.Tests.Core.ErrorsTests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.TestUtilities.Assertions

[<Fact>]
let ``WhisperError Message property returns correct messages`` () =
    let testCases = [
        (ModelLoadError "test.bin", "Failed to load model: test.bin")
        (ProcessingError(42, "test error"), "Processing error 42: test error")
        (InvalidAudioFormat "WAV required", "Invalid audio format: WAV required")
        (StateError "invalid state", "State error: invalid state")
        (NativeLibraryError "DLL not found", "Native library error: DLL not found")
        (TokenizationError "invalid token", "Tokenization error: invalid token")
        (ConfigurationError "invalid config", "Configuration error: invalid config")
        (FileNotFound "/path/to/file", "File not found: /path/to/file")
        (NetworkError "connection failed", "Network error: connection failed")
        (OutOfMemory, "Out of memory")
        (Cancelled, "Operation cancelled")
        (NotImplemented "streaming", "Feature not implemented: streaming")
    ]

    for error, expectedMessage in testCases do
        error.Message |> should equal expectedMessage

[<Fact>]
let ``Result mapError transforms error correctly`` () =
    let originalError = Error (ProcessingError(1, "original"))
    let mappedError = Result.mapError (fun _ -> ModelLoadError "mapped") originalError

    match mappedError with
    | Error (ModelLoadError msg) -> msg |> should equal "mapped"
    | _ -> failwith "Expected mapped error"

    // Test that Ok values are preserved
    let okValue = Ok 42
    let mappedOk = Result.mapError (fun _ -> ModelLoadError "should not happen") okValue

    match mappedOk with
    | Ok value -> value |> should equal 42
    | _ -> failwith "Expected Ok value"

[<Fact>]
let ``Result bindWithError chains operations correctly`` () =
    let successOperation value = Ok (value * 2)
    let failureOperation _ = Error (ProcessingError(1, "failed"))

    // Test successful chain
    let result1 = Ok 5 |> Result.bindWithError successOperation
    result1 |> shouldBeOk |> should equal 10

    // Test failure propagation
    let result2 = Error OutOfMemory |> Result.bindWithError successOperation
    result2 |> shouldBeError |> should equal OutOfMemory

    // Test failure in operation
    let result3 = Ok 5 |> Result.bindWithError failureOperation
    match result3 with
    | Error (ProcessingError(1, msg)) -> msg |> should equal "failed"
    | _ -> failwith "Expected processing error"

[<Fact>]
let ``Result tryWith catches exceptions correctly`` () =
    // Test successful operation
    let successFn x = x * 2
    let result1 = Result.tryWith successFn 5
    result1 |> shouldBeOk |> should equal 10

    // Test OutOfMemoryException
    let oomFn _ = raise (OutOfMemoryException())
    let result2 = Result.tryWith oomFn ()
    result2 |> shouldBeError |> should equal OutOfMemory

    // Test OperationCanceledException
    let cancelFn _ = raise (OperationCanceledException())
    let result3 = Result.tryWith cancelFn ()
    result3 |> shouldBeError |> should equal Cancelled

    // Test FileNotFoundException
    let fileNotFoundFn _ = raise (FileNotFoundException("", "test.txt"))
    let result4 = Result.tryWith fileNotFoundFn ()
    match result4 with
    | Error (FileNotFound path) -> path |> should equal "test.txt"
    | _ -> failwith "Expected FileNotFound error"

    // Test generic exception
    let genericFn _ = raise (Exception("generic error"))
    let result5 = Result.tryWith genericFn ()
    match result5 with
    | Error (ProcessingError(0, msg)) -> msg |> should equal "generic error"
    | _ -> failwith "Expected ProcessingError"

[<Fact>]
let ``Result tryAsync handles async exceptions correctly`` () =
    // Test successful async operation
    let successAsync() = async { return 42 }
    let result1 = Result.tryAsync successAsync |> Async.RunSynchronously
    result1 |> shouldBeOk |> should equal 42

    // Test async OutOfMemoryException
    let oomAsync() = async { raise (OutOfMemoryException()) }
    let result2 = Result.tryAsync oomAsync |> Async.RunSynchronously
    result2 |> shouldBeError |> should equal OutOfMemory

    // Test async OperationCanceledException
    let cancelAsync() = async { raise (OperationCanceledException()) }
    let result3 = Result.tryAsync cancelAsync |> Async.RunSynchronously
    result3 |> shouldBeError |> should equal Cancelled

[<Fact>]
let ``ErrorHandling mapNativeError maps codes correctly`` () =
    let testCases = [
        (-1, ModelLoadError "Failed to load model")
        (-2, InvalidAudioFormat "Invalid audio format")
        (-3, OutOfMemory)
        (-4, StateError "Invalid state")
        (-5, TokenizationError "Failed to tokenize")
    ]

    for code, expectedError in testCases do
        let mapped = ErrorHandling.mapNativeError code
        mapped |> should equal expectedError

    // Test unknown error code
    let unknownMapped = ErrorHandling.mapNativeError 999
    match unknownMapped with
    | ProcessingError(999, msg) -> msg |> should equal "Unknown error code: 999"
    | _ -> failwith "Expected ProcessingError for unknown code"

[<Fact>]
let ``ErrorHandling retryWithBackoff retries on retryable errors`` () =
    let mutable attempts = 0

    let operation() = async {
        attempts <- attempts + 1
        if attempts < 3 then
            return Error OutOfMemory
        else
            return Ok "success"
    }

    let result = ErrorHandling.retryWithBackoff operation 5 |> Async.RunSynchronously

    result |> shouldBeOk |> should equal "success"
    attempts |> should equal 3

[<Fact>]
let ``ErrorHandling retryWithBackoff does not retry non-retryable errors`` () =
    let mutable attempts = 0

    let operation() = async {
        attempts <- attempts + 1
        return Error (ModelLoadError "permanent failure")
    }

    let result = ErrorHandling.retryWithBackoff operation 5 |> Async.RunSynchronously

    result |> shouldBeError |> should equal (ModelLoadError "permanent failure")
    attempts |> should equal 1

[<Fact>]
let ``ErrorHandling validateConfig detects invalid configurations`` () =
    // Test valid configuration
    let validConfig = TestConfigBuilder().WithDefaults()
    let validResult = ErrorHandling.validateConfig validConfig
    validResult |> shouldBeOk |> should equal validConfig

    // Test invalid thread count
    let invalidThreadConfig = { validConfig with ThreadCount = 0 }
    let threadResult = ErrorHandling.validateConfig invalidThreadConfig
    match threadResult with
    | Error (ConfigurationError msg) -> msg |> should contain "Thread count"
    | _ -> failwith "Expected configuration error"

    // Test invalid chunk size
    let invalidChunkConfig = { validConfig with ChunkSizeMs = -100 }
    let chunkResult = ErrorHandling.validateConfig invalidChunkConfig
    match chunkResult with
    | Error (ConfigurationError msg) -> msg |> should contain "Chunk size"
    | _ -> failwith "Expected configuration error"

    // Test invalid overlap
    let invalidOverlapConfig = { validConfig with ChunkSizeMs = 1000; OverlapMs = 1500 }
    let overlapResult = ErrorHandling.validateConfig invalidOverlapConfig
    match overlapResult with
    | Error (ConfigurationError msg) -> msg |> should contain "Overlap"
    | _ -> failwith "Expected configuration error"

    // Test invalid confidence
    let invalidConfidenceConfig = { validConfig with MinConfidence = 1.5f }
    let confidenceResult = ErrorHandling.validateConfig invalidConfidenceConfig
    match confidenceResult with
    | Error (ConfigurationError msg) -> msg |> should contain "confidence"
    | _ -> failwith "Expected configuration error"

[<Fact>]
let ``ErrorHandling useResource ensures cleanup on error`` () =
    let mutable resourceAcquired = false
    let mutable resourceReleased = false

    let acquire() =
        resourceAcquired <- true
        "resource"

    let release _ =
        resourceReleased <- true

    let failingAction _ = async {
        raise (Exception("action failed"))
        return Ok ()
    }

    let result =
        ErrorHandling.useResource acquire release failingAction
        |> Async.RunSynchronously

    resourceAcquired |> should be True
    resourceReleased |> should be True

[<Fact>]
let ``ErrorHandling useResource ensures cleanup on success`` () =
    let mutable resourceReleased = false

    let acquire() = "resource"
    let release _ = resourceReleased <- true
    let successAction r = async { return Ok r }

    let result =
        ErrorHandling.useResource acquire release successAction
        |> Async.RunSynchronously

    result |> shouldBeOk |> should equal "resource"
    resourceReleased |> should be True

[<Theory>]
[<InlineData(-1)>]
[<InlineData(0)>]
[<InlineData(2000)>]
let ``Configuration validation detects invalid chunk sizes`` (chunkSize: int) =
    let config = { TestConfigBuilder().WithDefaults() with ChunkSizeMs = chunkSize }
    let result = ErrorHandling.validateConfig config

    if chunkSize <= 0 then
        result |> shouldBeErrorMatching (fun e ->
            match e with
            | ConfigurationError msg -> msg.Contains("Chunk size")
            | _ -> false)
        |> ignore
    else
        result |> shouldBeOk |> ignore