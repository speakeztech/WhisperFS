module WhisperFS.Tests.WhisperFSIntegrationTests

open System
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.TestHelpers

/// Integration tests for the main WhisperFS API
/// These test the overall library integration and API contracts
module WhisperFSApiTests =

    [<Fact>]
    let ``WhisperFS.initialize should return error when native library not available`` () =
        // Given: No native library available (expected in test environment)

        // When: Attempting to initialize
        let result = WhisperFS.initialize() |> Async.RunSynchronously

        // Then: Should return an error (since whisper.dll not available in test)
        match result with
        | Error _ -> () // Expected
        | Ok _ -> failwith "Expected error when native library not available"

    [<Fact>]
    let ``WhisperFS.createClient should create client with valid config`` () =
        // Given: A valid test configuration
        let config = TestData.defaultTestConfig

        // When: Creating a client
        let client = WhisperFS.createClient config

        // Then: Should create a client instance
        client |> should not' (be null)

        // And: Client should be disposable
        (client :> IDisposable).Dispose() // Should not throw

/// Tests for client functionality
module WhisperClientTests =

    [<Fact>]
    let ``IWhisperClient.ProcessAsync should return NotImplemented error`` () =
        // Given: A client and test audio samples
        let config = TestData.defaultTestConfig
        let client = WhisperFS.createClient config
        let samples = [| 0.0f; 0.1f; 0.2f |]

        // When: Processing audio
        let result = client.ProcessAsync(samples) |> Async.RunSynchronously

        // Then: Should return NotImplemented error (placeholder implementation)
        match result with
        | Error (NotImplemented _) -> () // Expected
        | _ -> failwith "Expected NotImplemented error"

    [<Fact>]
    let ``IWhisperClient.ProcessFileAsync should return NotImplemented error`` () =
        // Given: A client and test file path
        let config = TestData.defaultTestConfig
        let client = WhisperFS.createClient config
        let testPath = "/test/audio.wav"

        // When: Processing file
        let result = client.ProcessFileAsync(testPath) |> Async.RunSynchronously

        // Then: Should return NotImplemented error (placeholder implementation)
        match result with
        | Error (NotImplemented _) -> () // Expected
        | _ -> failwith "Expected NotImplemented error"

    [<Fact>]
    let ``IWhisperClient.Reset should return Ok`` () =
        // Given: A client
        let config = TestData.defaultTestConfig
        let client = WhisperFS.createClient config

        // When: Resetting client
        let result = client.Reset()

        // Then: Should return Ok (simple implementation)
        match result with
        | Ok () -> () // Expected
        | Error err -> failwithf "Expected Ok but got Error: %A" err

    [<Fact>]
    let ``IWhisperClient.DetectLanguageAsync should return NotImplemented error`` () =
        // Given: A client and test samples
        let config = TestData.defaultTestConfig
        let client = WhisperFS.createClient config
        let samples = [| 0.0f; 0.1f; 0.2f |]

        // When: Detecting language
        let result = client.DetectLanguageAsync(samples) |> Async.RunSynchronously

        // Then: Should return NotImplemented error (placeholder implementation)
        match result with
        | Error (NotImplemented _) -> () // Expected
        | _ -> failwith "Expected NotImplemented error"

/// Tests for input/output types
module InputOutputTests =

    [<Fact>]
    let ``WhisperInput types should be properly discriminated`` () =
        // Given: Different input types
        let batchInput = BatchAudio [| 1.0f; 2.0f |]
        let fileInput = AudioFile "/test/file.wav"

        // When/Then: Types should be different
        match batchInput with
        | BatchAudio samples -> samples |> should equal [| 1.0f; 2.0f |]
        | _ -> failwith "Expected BatchAudio"

        match fileInput with
        | AudioFile path -> path |> should equal "/test/file.wav"
        | _ -> failwith "Expected AudioFile"

    [<Fact>]
    let ``IWhisperClient.Process should handle different input types`` () =
        // Given: A client and different input types
        let config = TestData.defaultTestConfig
        let client = WhisperFS.createClient config
        let batchInput = BatchAudio [| 1.0f; 2.0f |]
        let fileInput = AudioFile "/test/file.wav"

        // When: Processing different input types
        let batchOutput = client.Process(batchInput)
        let fileOutput = client.Process(fileInput)

        // Then: Should return appropriate output types
        match batchOutput with
        | BatchResult _ -> () // Expected
        | _ -> failwith "Expected BatchResult for BatchAudio"

        match fileOutput with
        | BatchResult _ -> () // Expected for AudioFile
        | _ -> failwith "Expected BatchResult for AudioFile"

/// Performance and resource tests
module ResourceTests =

    [<Fact>]
    let ``Multiple clients should be createable without issues`` () =
        // Given: Multiple client configurations
        let config = TestData.defaultTestConfig

        // When: Creating multiple clients
        let client1 = WhisperFS.createClient config
        let client2 = WhisperFS.createClient config
        let client3 = WhisperFS.createClient config

        // Then: All should be created successfully
        [client1; client2; client3] |> List.iter (fun c -> c |> should not' (be null))

        // And: All should be disposable
        [client1; client2; client3] |> List.iter (fun c -> (c :> IDisposable).Dispose())

    [<Fact>]
    let ``Client should provide observable events stream`` () =
        // Given: A client
        let config = TestData.defaultTestConfig
        let client = WhisperFS.createClient config

        // When: Accessing events
        let events = client.Events

        // Then: Should provide observable
        events |> should not' (be null)

        // And: Should be subscribable (basic check)
        use subscription = events.Subscribe(fun _ -> ())
        subscription |> should not' (be null)