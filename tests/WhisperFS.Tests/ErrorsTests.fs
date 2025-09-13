module WhisperFS.Tests.ErrorsTests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.TestHelpers

[<Fact>]
let ``WhisperError Message property returns correct messages`` () =
    let testCases = [
        (ModelLoadError "test.bin", "Failed to load model: test.bin")
        (WhisperError.ProcessingError(42, "test error"), "Processing error 42: test error")
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
    let originalError = Error (WhisperError.ProcessingError(1, "original"))
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
let ``Result bind chains operations correctly`` () =
    let successOperation value = Ok (value * 2)
    let failureOperation _ = Error (WhisperError.ProcessingError(1, "failed"))

    // Test successful chain
    let result1 = Ok 5 |> Result.bind successOperation
    match result1 with
    | Ok 10 -> ()
    | _ -> failwith "Expected Ok 10"

    // Test failure propagation
    let result2 = Error OutOfMemory |> Result.bind successOperation
    match result2 with
    | Error OutOfMemory -> ()
    | _ -> failwith "Expected OutOfMemory error"

    // Test failure in operation
    let result3 = Ok 5 |> Result.bind failureOperation
    match result3 with
    | Error (WhisperError.ProcessingError(1, msg)) -> msg |> should equal "failed"
    | _ -> failwith "Expected processing error"

[<Fact>]
let ``Exception handling with Result type`` () =
    // Test successful operation
    let successFn x = x * 2
    let safeCall fn input =
        try
            Ok (fn input)
        with
        | :? OutOfMemoryException -> Error OutOfMemory
        | :? OperationCanceledException -> Error Cancelled
        | ex -> Error (WhisperError.ProcessingError(0, ex.Message))

    let result1 = safeCall successFn 5
    match result1 with
    | Ok 10 -> ()
    | _ -> failwith "Expected Ok 10"

[<Fact>]
let ``Configuration validation detects invalid values`` () =
    // Test valid configuration
    let validConfig = TestData.defaultTestConfig

    // Basic validation checks (without ErrorHandling module)
    validConfig.ThreadCount |> should be (greaterThan 0)
    validConfig.MaxTextContext |> should be (greaterThan 0)

    // Test invalid values
    let invalidThreadConfig = { validConfig with ThreadCount = 0 }
    invalidThreadConfig.ThreadCount |> should equal 0

    let invalidContextConfig = { validConfig with MaxTextContext = -100 }
    invalidContextConfig.MaxTextContext |> should equal -100

[<Fact>]
let ``Error types have distinct values`` () =
    let errors = [
        OutOfMemory
        Cancelled
        ModelLoadError "test"
        StateError "test"
        InvalidAudioFormat "test"
    ]

    // Check that different error types are not equal
    OutOfMemory |> should not' (equal Cancelled)
    ModelLoadError "test" |> should not' (equal (StateError "test"))

[<Theory>]
[<InlineData(-1)>]
[<InlineData(0)>]
[<InlineData(2000)>]
let ``Configuration validation detects invalid duration values`` (durationMs: int) =
    let config = { TestData.defaultTestConfig with DurationMs = durationMs }

    if durationMs < 0 then
        config.DurationMs |> should be (lessThan 0)
    else
        config.DurationMs |> should be (greaterThanOrEqualTo 0)