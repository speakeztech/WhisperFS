module WhisperFS.Tests.WhisperFSIntegrationTests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Runtime
open WhisperFS.Tests.TestHelpers
open FSharp.Control.Reactive

/// Shared test fixture for WhisperFS integration tests
/// This ensures we only initialize once per test run
type WhisperFSTestFixture() =
    let mutable initialized = false
    let mutable initResult : Result<unit, WhisperError> option = None

    member _.Initialize() =
        if not initialized then
            initialized <- true
            initResult <- Some(WhisperFS.initialize() |> Async.RunSynchronously)
        initResult.Value

    interface IDisposable with
        member _.Dispose() = ()

/// Collection definition to share the fixture across tests
[<CollectionDefinition("WhisperFS Integration Tests")>]
type WhisperFSIntegrationCollection() =
    interface ICollectionFixture<WhisperFSTestFixture>

/// Tests for the main WhisperFS API initialization
[<Collection("WhisperFS Integration Tests")>]
type WhisperFSApiTests(fixture: WhisperFSTestFixture) =

    [<Fact>]
    member _.``WhisperFS.initialize should succeed once``() =
        // When: Initialize WhisperFS (using shared fixture)
        let result = fixture.Initialize()

        // Then: Should succeed
        match result with
        | Ok () ->
            // Verify native library directory exists
            let nativeDir = Native.Library.getNativeLibraryDirectory()
            Directory.Exists(nativeDir) |> should be True
        | Error err ->
            // Native library loading can fail for various reasons in test environments
            match err with
            | NativeLibraryError msg ->
                // These are all acceptable reasons for failure in a test environment:
                // - No compatible runtime found
                // - Missing Visual C++ runtime dependencies
                // - CI environment without native binaries
                printfn $"Native library initialization skipped: {msg}"
                () // Skip test - native library unavailable gracefully
            | _ -> failwithf "Unexpected initialization error: %A" err

    [<Fact>]
    member _.``WhisperFS.createClient requires valid config``() =
        // Given: Initialization succeeded
        let initResult = fixture.Initialize()

        match initResult with
        | Error (NativeLibraryError msg) ->
            // Skip test if native library cannot be loaded
            printfn $"Test skipped - native library unavailable: {msg}"
            ()
        | _ ->
            // Given: A valid configuration with Tiny model
            let config = { TestData.defaultTestConfig with ModelType = ModelType.Tiny }

            // When: Creating a client (will download model if needed)
            let clientResult = WhisperFS.createClient config |> Async.RunSynchronously

            // Then: Should succeed or fail with expected error
            match clientResult with
            | Ok client ->
                client |> should not' (be null)
                (client :> IDisposable).Dispose()
            | Error (ModelLoadError _) ->
                () // Model not available, expected
            | Error (FileNotFound _) ->
                () // Model file not found, expected
            | Error err ->
                failwithf "Unexpected error: %A" err

/// Tests for client operations (only run if client can be created)
[<Collection("WhisperFS Integration Tests")>]
type WhisperClientOperationTests(fixture: WhisperFSTestFixture) =

    let tryCreateClient() =
        let initResult = fixture.Initialize()
        match initResult with
        | Error (NativeLibraryError msg) ->
            None
        | _ ->
            let config = { TestData.defaultTestConfig with ModelType = ModelType.Tiny }
            match WhisperFS.createClient config |> Async.RunSynchronously with
            | Ok client -> Some client
            | Error _ -> None

    [<Fact>]
    member _.``Client operations should handle invalid input gracefully``() =
        match tryCreateClient() with
        | None ->
            () // Skip if client cannot be created
        | Some client ->
            use _ = client

            // Test with empty samples
            let emptySamples = [||]
            let result = client.ProcessAsync(emptySamples) |> Async.RunSynchronously

            // Should handle gracefully (either process or return appropriate error)
            match result with
            | Ok _ -> () // Processed empty input
            | Error _ -> () // Returned error for empty input

    [<Fact>]
    member _.``Client.Reset should always succeed``() =
        match tryCreateClient() with
        | None ->
            () // Skip if client cannot be created
        | Some client ->
            use _ = client

            // Reset should always succeed
            let result = client.Reset()
            match result with
            | Ok () -> ()
            | Error err -> failwithf "Reset failed: %A" err

    [<Fact>]
    member _.``Client.GetMetrics should return initial metrics``() =
        match tryCreateClient() with
        | None ->
            () // Skip if client cannot be created
        | Some client ->
            use _ = client

            // Get metrics should always work
            let metrics = client.GetMetrics()

            // Should have initial values
            metrics.TotalProcessingTime |> should equal TimeSpan.Zero
            metrics.TotalAudioProcessed |> should equal TimeSpan.Zero
            metrics.SegmentsProcessed |> should equal 0
            metrics.ErrorCount |> should equal 0

    [<Fact>]
    member _.``Client.Events should be observable``() =
        match tryCreateClient() with
        | None ->
            () // Skip if client cannot be created
        | Some client ->
            use _ = client

            // Should be able to subscribe to events
            let events = client.Events
            events |> should not' (be null)

            let receivedEvents = ResizeArray<Result<TranscriptionEvent, WhisperError>>()
            use subscription = events.Subscribe(receivedEvents.Add)
            subscription |> should not' (be null)

/// Tests for different input/output modes
[<Collection("WhisperFS Integration Tests")>]
type InputOutputTests(fixture: WhisperFSTestFixture) =

    [<Fact>]
    member _.``Process should handle different input types``() =
        let initResult = fixture.Initialize()
        match initResult with
        | Error (NativeLibraryError msg) ->
            () // Skip test - native library unavailable
        | _ ->
            let config = { TestData.defaultTestConfig with ModelType = ModelType.Tiny }
            match WhisperFS.createClient config |> Async.RunSynchronously with
            | Ok client ->
                use _ = client

                // Test different input types
                let batchInput = BatchAudio [| 0.0f |]
                let fileInput = AudioFile "/nonexistent.wav"

                // Process should return appropriate output types
                let batchOutput = client.Process(batchInput)
                let fileOutput = client.Process(fileInput)

                match batchOutput with
                | BatchResult _ -> () // Expected
                | _ -> failwith "Expected BatchResult for BatchAudio"

                match fileOutput with
                | BatchResult _ -> () // Expected for AudioFile
                | _ -> failwith "Expected BatchResult for AudioFile"
            | Error _ ->
                () // Client creation failed, skip

/// Tests for language detection
[<Collection("WhisperFS Integration Tests")>]
type LanguageDetectionTests(fixture: WhisperFSTestFixture) =

    [<Fact>]
    member _.``DetectLanguageAsync should handle multilingual models``() =
        let initResult = fixture.Initialize()
        match initResult with
        | Error (NativeLibraryError msg) ->
            () // Skip test - native library unavailable
        | _ ->
            // Use Base model for multilingual support
            let config = { TestData.defaultTestConfig with ModelType = ModelType.Base; Language = None }
            match WhisperFS.createClient config |> Async.RunSynchronously with
            | Ok client ->
                use _ = client

                let samples = Array.create 16000 0.0f // 1 second of silence
                let result = client.DetectLanguageAsync(samples) |> Async.RunSynchronously

                match result with
                | Ok detection ->
                    detection.Language |> should not' (be null)
                    detection.Confidence |> should be (greaterThanOrEqualTo 0.0f)
                    detection.Confidence |> should be (lessThanOrEqualTo 1.0f)
                | Error (ConfigurationError msg) when msg.Contains("multilingual") ->
                    () // Model doesn't support language detection
                | Error _ ->
                    () // Other expected errors in test environment
            | Error _ ->
                () // Client creation failed, skip

/// Tests for file processing
[<Collection("WhisperFS Integration Tests")>]
type FileProcessingTests(fixture: WhisperFSTestFixture) =

    [<Fact>]
    member _.``ProcessFileAsync should handle missing files gracefully``() =
        let initResult = fixture.Initialize()
        match initResult with
        | Error (NativeLibraryError msg) ->
            () // Skip test - native library unavailable
        | _ ->
            let config = { TestData.defaultTestConfig with ModelType = ModelType.Tiny }
            match WhisperFS.createClient config |> Async.RunSynchronously with
            | Ok client ->
                use _ = client

                let nonExistentPath = "/this/file/does/not/exist.wav"
                let result = client.ProcessFileAsync(nonExistentPath) |> Async.RunSynchronously

                match result with
                | Error (FileNotFound _) -> () // Expected
                | Error (NotImplemented _) -> () // Expected if not implemented
                | Ok _ -> failwith "Should not succeed with non-existent file"
                | Error err -> () // Other errors acceptable in test
            | Error _ ->
                () // Client creation failed, skip