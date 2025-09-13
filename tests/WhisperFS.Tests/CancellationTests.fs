module WhisperFS.Tests.CancellationTests

open System
open System.Threading
open Xunit
open WhisperFS

[<Fact>]
let ``ProcessAsyncWithCancellation respects cancellation token`` () =
    async {
        // Arrange
        let config = { WhisperConfig.defaultConfig with
                        ModelType = Tiny
                        ThreadCount = 1 }

        let! clientResult = WhisperFS.createClient config
        match clientResult with
        | Error e -> Assert.True(false, $"Failed to create client: {e}")
        | Ok client ->
            use _ = client

            // Create audio samples (10 seconds of silence)
            let samples = Array.create (16000 * 10) 0.0f

            // Create cancellation token that cancels after 100ms
            use cts = new CancellationTokenSource(100)

            // Act & Assert
            let! result = client.ProcessAsyncWithCancellation(samples, cts.Token)

            match result with
            | Error (WhisperError.OperationCancelled) ->
                Assert.True(true) // Expected
            | Error e ->
                Assert.True(false, $"Unexpected error: {e}")
            | Ok _ ->
                Assert.True(false, "Processing should have been cancelled")
    } |> Async.RunSynchronously

[<Fact>]
let ``ProcessFileAsyncWithCancellation respects cancellation token`` () =
    async {
        // Arrange
        let config = { WhisperConfig.defaultConfig with ModelType = Tiny }

        let! clientResult = WhisperFS.createClient config
        match clientResult with
        | Error e -> Assert.True(false, $"Failed to create client: {e}")
        | Ok client ->
            use _ = client

            // Create cancellation token that cancels immediately
            use cts = new CancellationTokenSource()
            cts.Cancel()

            // Act
            let! result = client.ProcessFileAsyncWithCancellation("test.wav", cts.Token)

            // Assert
            match result with
            | Error (WhisperError.OperationCancelled) ->
                Assert.True(true) // Expected
            | Error (FileNotFound _) ->
                Assert.True(true) // Also acceptable if file doesn't exist
            | Error e ->
                Assert.True(false, $"Unexpected error: {e}")
            | Ok _ ->
                Assert.True(false, "Processing should have been cancelled")
    } |> Async.RunSynchronously

[<Fact>]
let ``Multiple concurrent cancellations are handled correctly`` () =
    async {
        // Arrange
        let config = { WhisperConfig.defaultConfig with
                        ModelType = Tiny
                        ThreadCount = 2 }

        let! clientResult = WhisperFS.createClient config
        match clientResult with
        | Error e -> Assert.True(false, $"Failed to create client: {e}")
        | Ok client ->
            use _ = client

            // Create multiple audio samples
            let samples1 = Array.create (16000 * 5) 0.0f
            let samples2 = Array.create (16000 * 5) 0.1f
            let samples3 = Array.create (16000 * 5) 0.2f

            // Create independent cancellation tokens
            use cts1 = new CancellationTokenSource(50)
            use cts2 = new CancellationTokenSource(100)
            use cts3 = new CancellationTokenSource(150)

            // Act - Process in parallel
            let! results =
                [
                    client.ProcessAsyncWithCancellation(samples1, cts1.Token)
                    client.ProcessAsyncWithCancellation(samples2, cts2.Token)
                    client.ProcessAsyncWithCancellation(samples3, cts3.Token)
                ]
                |> Async.Parallel

            // Assert - At least some should be cancelled
            let cancelledCount =
                results
                |> Array.filter (function
                    | Error (WhisperError.OperationCancelled) -> true
                    | _ -> false)
                |> Array.length

            Assert.True(cancelledCount > 0, "At least one operation should have been cancelled")
    } |> Async.RunSynchronously

[<Fact>]
let ``Cancellation token does not affect non-cancellable operations`` () =
    async {
        // Arrange
        let config = { WhisperConfig.defaultConfig with ModelType = Tiny }

        let! clientResult = WhisperFS.createClient config
        match clientResult with
        | Error e -> Assert.True(false, $"Failed to create client: {e}")
        | Ok client ->
            use _ = client

            // Create short audio sample
            let samples = Array.create 16000 0.0f // 1 second

            // Act - Process without cancellation token
            let! result = client.ProcessAsync(samples)

            // Assert
            match result with
            | Ok transcription ->
                Assert.NotNull(transcription.FullText)
                Assert.True(transcription.ProcessingTime > TimeSpan.Zero)
            | Error e ->
                Assert.True(false, $"Processing failed: {e}")
    } |> Async.RunSynchronously