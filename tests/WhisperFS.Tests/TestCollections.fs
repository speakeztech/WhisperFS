module WhisperFS.Tests.TestCollections

open System
open Xunit
open WhisperFS

/// Fixture that provides a factory for creating WhisperClient instances
/// Each test gets its own client to avoid thread-safety issues
type WhisperClientFixture() =

    /// Create a new client for a test
    member _.CreateClient() =
        let config = { WhisperConfig.defaultConfig with ModelType = Tiny }
        WhisperFS.createClient config |> Async.RunSynchronously

    interface IDisposable with
        member _.Dispose() = ()

/// Collection for tests that require model downloads or native library access
/// Tests in this collection will run sequentially to avoid file conflicts
[<CollectionDefinition("WhisperFS Integration Tests", DisableParallelization = true)>]
type WhisperFSIntegrationCollection() =
    interface ICollectionFixture<WhisperClientFixture>