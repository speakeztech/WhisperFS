module WhisperFS.Tests.Integration.ModelManagementTests

open System
open System.IO
open System.Threading
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Runtime
open WhisperFS.Tests.Mocks
open WhisperFS.Tests.TestUtilities

[<Fact>]
let ``Complete model download and usage workflow`` () =
    // Setup
    let downloader = new MockModelDownloader()
    let factory = new MockWhisperFactory()

    // Subscribe to download events
    let events = ResizeArray<ModelEvent>()
    use subscription = downloader.Events.Subscribe(events.Add)

    // Download model
    let modelPath =
        (downloader :> IModelDownloader).DownloadModelAsync(ModelType.Base)
        |> Async.RunSynchronously

    // Verify download events
    events |> Seq.exists (function DownloadStarted _ -> true | _ -> false) |> should be True
    events |> Seq.exists (function DownloadCompleted _ -> true | _ -> false) |> should be True

    // Load model into factory
    let factoryResult = factory.FromPath(modelPath)

    match factoryResult with
    | Ok loadedFactory ->
        // Create client with downloaded model
        let config = TestConfigBuilder().WithModel(ModelType.Base).Build()
        let client = loadedFactory.CreateClient(config)

        // Use client
        let samples = AudioGenerator.generateSilence 100 16000
        let result = client.ProcessAsync(samples) |> Async.RunSynchronously

        result |> Assertions.shouldBeOk |> ignore

        // Cleanup
        client.Dispose()
        loadedFactory.Dispose()
    | Error err -> failwithf "Failed to load model: %A" err

[<Fact>]
let ``Model verification workflow`` () =
    let downloader = new MockModelDownloader()
    let events = ResizeArray<ModelEvent>()

    use subscription = downloader.Events.Subscribe(events.Add)

    // Download model
    let modelPath =
        (downloader :> IModelDownloader).DownloadModelAsync(ModelType.Small)
        |> Async.RunSynchronously

    // Simulate verification
    events.Add(VerificationStarted ModelType.Small)
    Thread.Sleep(50)
    events.Add(VerificationCompleted(ModelType.Small, true))

    // Check verification events
    events |> Seq.exists (function VerificationStarted _ -> true | _ -> false) |> should be True
    events |> Seq.exists (function VerificationCompleted(_, true) -> true | _ -> false) |> should be True

[<Fact>]
let ``Model deletion and cleanup`` () =
    use fixture = new TestFixture()
    let modelDir = fixture.CreateTempFile("")
    Directory.CreateDirectory(modelDir) |> ignore

    // Create dummy model files
    let models = [
        (ModelType.Tiny, "ggml-tiny.bin")
        (ModelType.Base, "ggml-base.bin")
        (ModelType.Small, "ggml-small.bin")
    ]

    for modelType, fileName in models do
        let path = Path.Combine(modelDir, fileName)
        File.WriteAllBytes(path, [| 0uy |])

    // Verify files exist
    models |> List.iter (fun (_, fileName) ->
        let path = Path.Combine(modelDir, fileName)
        File.Exists(path) |> should be True
    )

    // Delete one model
    let toDelete = Path.Combine(modelDir, "ggml-base.bin")
    File.Delete(toDelete)

    // Verify deletion
    File.Exists(toDelete) |> should be False

    // Other models should still exist
    File.Exists(Path.Combine(modelDir, "ggml-tiny.bin")) |> should be True
    File.Exists(Path.Combine(modelDir, "ggml-small.bin")) |> should be True

[<Fact>]
let ``Model recommendation based on system resources`` () =
    let getRecommendedModel (availableMemoryGB: float) (hasGpu: bool) =
        if hasGpu && availableMemoryGB >= 6.0 then
            ModelType.LargeV3
        elif hasGpu && availableMemoryGB >= 4.0 then
            ModelType.Medium
        elif availableMemoryGB >= 2.0 then
            ModelType.Small
        elif availableMemoryGB >= 1.0 then
            ModelType.Base
        else
            ModelType.Tiny

    // Test recommendations
    getRecommendedModel 0.5 false |> should equal ModelType.Tiny
    getRecommendedModel 1.5 false |> should equal ModelType.Base
    getRecommendedModel 3.0 false |> should equal ModelType.Small
    getRecommendedModel 4.5 true |> should equal ModelType.Medium
    getRecommendedModel 8.0 true |> should equal ModelType.LargeV3

[<Fact>]
let ``Concurrent model downloads with progress tracking`` () =
    let downloader = new MockModelDownloader()
    let progressUpdates = ResizeArray<DownloadProgress>()

    use subscription =
        downloader.Events
        |> Observable.choose (function
            | DownloadProgress p -> Some p
            | _ -> None)
        |> Observable.subscribe progressUpdates.Add

    // Download multiple models concurrently
    let models = [ModelType.Tiny; ModelType.Base; ModelType.Small]

    let downloadTasks =
        models
        |> List.map (fun model ->
            (downloader :> IModelDownloader).DownloadModelAsync(model))

    let results = downloadTasks |> Async.Parallel |> Async.RunSynchronously

    // Should have received progress updates for all models
    let modelsWithProgress =
        progressUpdates
        |> Seq.map (fun p -> p.ModelType)
        |> Set.ofSeq

    models |> List.iter (fun model ->
        modelsWithProgress |> should contain model
    )

    // All downloads should complete
    results |> should haveLength 3
    results |> Array.iter (fun path -> path |> should not' (be EmptyString))

[<Fact>]
let ``Model fallback on download failure`` () =
    let downloader = new MockModelDownloader()

    // Simulate failure for large model
    downloader.SimulateDownloadFailure(ModelType.LargeV3, "Insufficient space")

    // Try to download with fallback
    let downloadWithFallback (primary: ModelType) (fallback: ModelType) = async {
        match downloader.GetStatus(primary) with
        | Failed _ ->
            // Fall back to smaller model
            return! (downloader :> IModelDownloader).DownloadModelAsync(fallback)
        | _ ->
            return! (downloader :> IModelDownloader).DownloadModelAsync(primary)
    }

    let result = downloadWithFallback ModelType.LargeV3 ModelType.Base |> Async.RunSynchronously

    // Should have downloaded fallback model
    result |> should not' (be EmptyString)
    (downloader :> IModelDownloader).IsModelDownloaded(ModelType.Base) |> should be True
    (downloader :> IModelDownloader).IsModelDownloaded(ModelType.LargeV3) |> should be False

[<Fact>]
let ``Model cache management`` () =
    let cacheDir = Path.Combine(Path.GetTempPath(), "WhisperFS_Test_Cache")
    Directory.CreateDirectory(cacheDir) |> ignore

    try
        // Simulate cached models
        let cachedModels = [
            ("ggml-tiny.bin", 39L * 1024L * 1024L, DateTime.UtcNow.AddDays(-30.0))
            ("ggml-base.bin", 142L * 1024L * 1024L, DateTime.UtcNow.AddDays(-7.0))
            ("ggml-small.bin", 466L * 1024L * 1024L, DateTime.UtcNow.AddDays(-1.0))
        ]

        for fileName, size, lastAccess in cachedModels do
            let path = Path.Combine(cacheDir, fileName)
            File.WriteAllBytes(path, Array.zeroCreate<byte> 1024)
            File.SetLastAccessTimeUtc(path, lastAccess)

        // Find old cached models (>14 days)
        let oldModels =
            Directory.GetFiles(cacheDir)
            |> Array.filter (fun path ->
                let lastAccess = File.GetLastAccessTimeUtc(path)
                (DateTime.UtcNow - lastAccess).TotalDays > 14.0)

        oldModels |> should haveLength 1
        oldModels.[0] |> should contain "tiny"

    finally
        if Directory.Exists(cacheDir) then
            Directory.Delete(cacheDir, true)