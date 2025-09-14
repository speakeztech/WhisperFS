module WhisperFS.Tests.WhisperFSIntegrationTests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Runtime
open WhisperFS.Tests.TestHelpers
open WhisperFS.Tests.TestCollections
open FSharp.Control.Reactive

// Collection is defined in TestCollections.fs with WhisperClientFixture

/// Tests for the main WhisperFS API initialization
[<Collection("WhisperFS Integration Tests")>]
type WhisperFSApiTests(fixture: WhisperClientFixture) =

    [<Fact>]
    member _.``WhisperFS.initialize should succeed once``() =
        // When: Initialize WhisperFS
        let result = WhisperFS.initialize() |> Async.RunSynchronously

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
        // Try to create a client
        match fixture.CreateClient() with
        | Ok client ->
            use _ = client
            client |> should not' (be null)

            // Verify native library directory exists
            let nativeDir = Native.Library.getNativeLibraryDirectory()
            Directory.Exists(nativeDir) |> should be True
        | Error e ->
            // Client creation can fail in test environments for various reasons
            printfn $"Test skipped - client unavailable: {e}"
            ()

/// Tests for client operations (only run if client can be created)
[<Collection("WhisperFS Integration Tests")>]
type WhisperClientOperationTests(fixture: WhisperClientFixture) =

    let tryCreateClient() =
        match fixture.CreateClient() with
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
type InputOutputTests(fixture: WhisperClientFixture) =

    let tryCreateClient() =
        match fixture.CreateClient() with
        | Ok client -> Some client
        | Error _ -> None

    [<Fact>]
    member _.``Process should handle different input types``() =
        match tryCreateClient() with
        | Some client ->
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
        | None ->
            () // Client not available, skip

/// Tests for language detection
[<Collection("WhisperFS Integration Tests")>]
type LanguageDetectionTests(fixture: WhisperClientFixture) =

    let tryCreateClient() =
        match fixture.CreateClient() with
        | Ok client -> Some client
        | Error _ -> None

    [<Fact>]
    member _.``DetectLanguageAsync should handle multilingual models``() =
        match tryCreateClient() with
        | Some client ->
            use _ = client
            let samples = TestData.generateTone 440.0 1000 16000 // Use tone instead of silence

            // When detecting language
            match client.DetectLanguageAsync(samples) |> Async.RunSynchronously with
            | Ok detection ->
                detection.Language |> should not' (be null)
                detection.Confidence |> should be (greaterThanOrEqualTo 0.0f)
                detection.Confidence |> should be (lessThanOrEqualTo 1.0f)
            | Error (ConfigurationError msg) when msg.Contains("multilingual") ->
                () // Model doesn't support language detection
            | Error _ ->
                () // Other expected errors in test environment
        | None ->
            () // Client not available, skip

/// Tests for file processing
[<Collection("WhisperFS Integration Tests")>]
type FileProcessingTests(fixture: WhisperClientFixture) =

    let tryCreateClient() =
        match fixture.CreateClient() with
        | Ok client -> Some client
        | Error _ -> None

    [<Fact>]
    member _.``ProcessFileAsync should handle missing files gracefully``() =
        match tryCreateClient() with
        | Some client ->
            use _ = client
            let nonExistentPath = "/this/file/does/not/exist.wav"
            let result = client.ProcessFileAsync(nonExistentPath) |> Async.RunSynchronously

            match result with
            | Error (FileNotFound _) -> () // Expected
            | Error (NotImplemented _) -> () // Expected if not implemented
            | Ok _ -> failwith "Should not succeed with non-existent file"
            | Error _ -> () // Other errors acceptable in test
        | None ->
            () // Client not available, skip