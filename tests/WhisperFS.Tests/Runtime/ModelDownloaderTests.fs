module WhisperFS.Tests.Runtime.ModelDownloaderTests

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
let ``MockModelDownloader downloads model successfully`` () =
    let downloader = MockModelDownloader()

    let result =
        (downloader :> IModelDownloader).DownloadModelAsync(ModelType.Base)
        |> Async.RunSynchronously

    result |> should not' (be EmptyString)
    downloader.GetStatus(ModelType.Base) |> should equal (Downloaded(result, ModelType.Base.GetModelSize()))

[<Fact>]
let ``MockModelDownloader tracks download progress`` () =
    let downloader = MockModelDownloader()
    let progressEvents = ResizeArray<ModelEvent>()

    use subscription = downloader.Events.Subscribe(progressEvents.Add)

    (downloader :> IModelDownloader).DownloadModelAsync(ModelType.Small)
    |> Async.RunSynchronously
    |> ignore

    // Should have start, progress updates, and completion
    progressEvents |> Seq.filter (function DownloadStarted _ -> true | _ -> false) |> should haveLength 1
    progressEvents |> Seq.filter (function DownloadProgress _ -> true | _ -> false) |> should not' (be Empty)
    progressEvents |> Seq.filter (function DownloadCompleted _ -> true | _ -> false) |> should haveLength 1

[<Fact>]
let ``MockModelDownloader IsModelDownloaded returns correct status`` () =
    let downloader = MockModelDownloader()
    let iface = downloader :> IModelDownloader

    // Initially not downloaded
    iface.IsModelDownloaded(ModelType.Medium) |> should be False

    // Download the model
    iface.DownloadModelAsync(ModelType.Medium)
    |> Async.RunSynchronously
    |> ignore

    // Now should be downloaded
    iface.IsModelDownloaded(ModelType.Medium) |> should be True

[<Fact>]
let ``MockModelDownloader GetModelPath returns path for downloaded models`` () =
    let downloader = MockModelDownloader()
    let iface = downloader :> IModelDownloader

    // No path before download
    iface.GetModelPath(ModelType.Tiny) |> should equal ""

    // Download the model
    let downloadedPath =
        iface.DownloadModelAsync(ModelType.Tiny)
        |> Async.RunSynchronously

    // Path should match
    iface.GetModelPath(ModelType.Tiny) |> should equal downloadedPath

[<Fact>]
let ``MockModelDownloader handles download failure`` () =
    let downloader = MockModelDownloader()
    let failureEvents = ResizeArray<ModelEvent>()

    use subscription = downloader.Events.Subscribe(failureEvents.Add)

    // Simulate failure
    downloader.SimulateDownloadFailure(ModelType.LargeV1, "Disk full")

    // Check status
    match downloader.GetStatus(ModelType.LargeV1) with
    | Failed error -> error |> should equal "Disk full"
    | _ -> failwith "Expected Failed status"

    // Check event
    failureEvents |> Seq.exists (function
        | DownloadFailed(model, error) ->
            model = ModelType.LargeV1 && error = "Disk full"
        | _ -> false) |> should be True

[<Fact>]
let ``Download progress percentage calculation`` () =
    let progress = {
        ModelType = ModelType.Base
        BytesDownloaded = 71L * 1024L * 1024L
        TotalBytes = 142L * 1024L * 1024L
        PercentComplete = 50.0
        BytesPerSecond = 1048576.0
        EstimatedTimeRemaining = Some (TimeSpan.FromSeconds(71.0))
    }

    progress.PercentComplete |> should equal 50.0
    float progress.BytesDownloaded / float progress.TotalBytes * 100.0 |> should equal 50.0

[<Fact>]
let ``Model path generation is consistent`` () =
    let getModelPath (modelType: ModelType) =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WhisperFS",
            "models",
            sprintf "%s.bin" (modelType.GetModelName())
        )

    let path1 = getModelPath ModelType.Base
    let path2 = getModelPath ModelType.Base

    path1 |> should equal path2
    path1 |> should contain "ggml-base.bin"

[<Fact>]
let ``Concurrent downloads are handled correctly`` () =
    let downloader = MockModelDownloader()
    let iface = downloader :> IModelDownloader

    let models = [ModelType.Tiny; ModelType.Base; ModelType.Small]

    let downloadTasks =
        models
        |> List.map (fun model ->
            async {
                return! iface.DownloadModelAsync(model)
            })

    let results = downloadTasks |> Async.Parallel |> Async.RunSynchronously

    results |> should haveLength 3
    results |> Array.iter (fun path -> path |> should not' (be EmptyString))

    // All models should be downloaded
    models |> List.iter (fun model ->
        iface.IsModelDownloaded(model) |> should be True
    )

[<Fact>]
let ``Download cancellation support`` () =
    use cts = new CancellationTokenSource()

    let downloadTask = async {
        cts.CancelAfter(50)
        do! Async.Sleep 100
        return "Should not reach here"
    }

    let result =
        try
            Async.RunSynchronously(downloadTask, cancellationToken = cts.Token)
            |> Some
        with
        | :? OperationCanceledException -> None

    result |> should equal None

[<Fact>]
let ``Model verification events`` () =
    let events = ResizeArray<ModelEvent>()

    events.Add(VerificationStarted ModelType.Base)
    events.Add(VerificationCompleted(ModelType.Base, true))

    events |> should haveLength 2

    match events.[0] with
    | VerificationStarted model -> model |> should equal ModelType.Base
    | _ -> failwith "Wrong event"

    match events.[1] with
    | VerificationCompleted(model, success) ->
        model |> should equal ModelType.Base
        success |> should be True
    | _ -> failwith "Wrong event"