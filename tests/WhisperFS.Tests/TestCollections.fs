module WhisperFS.Tests.TestCollections

open Xunit

/// Collection for tests that require model downloads or native library access
/// Tests in this collection will run sequentially to avoid file conflicts
[<CollectionDefinition("WhisperFS Integration Tests", DisableParallelization = true)>]
type WhisperFSIntegrationCollection() =
    interface ICollectionFixture<unit>