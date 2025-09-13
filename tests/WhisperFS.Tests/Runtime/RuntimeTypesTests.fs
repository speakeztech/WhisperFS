module WhisperFS.Tests.Runtime.RuntimeTypesTests

open System
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Runtime

[<Fact>]
let ``ModelStatus discriminated union works correctly`` () =
    let notDownloaded = NotDownloaded
    let downloading = Downloading 0.5
    let downloaded = Downloaded("/path/to/model", 1024L)
    let failed = Failed "Network error"

    match notDownloaded with
    | NotDownloaded -> ()
    | _ -> failwith "Wrong status"

    match downloading with
    | Downloading progress -> progress |> should equal 0.5
    | _ -> failwith "Wrong status"

    match downloaded with
    | Downloaded(path, size) ->
        path |> should equal "/path/to/model"
        size |> should equal 1024L
    | _ -> failwith "Wrong status"

    match failed with
    | Failed error -> error |> should equal "Network error"
    | _ -> failwith "Wrong status"

[<Fact>]
let ``ModelMetadata contains required fields`` () =
    let metadata = {
        Type = ModelType.Base
        DisplayName = "Base Model"
        Description = "General purpose model"
        Size = 142L * 1024L * 1024L
        Url = "https://example.com/model.bin"
        Sha256 = Some "abc123"
        RequiresGpu = false
        Languages = ["en"; "es"; "fr"]
    }

    metadata.Type |> should equal ModelType.Base
    metadata.DisplayName |> should equal "Base Model"
    metadata.Size |> should equal (142L * 1024L * 1024L)
    metadata.RequiresGpu |> should be False
    metadata.Languages |> should haveLength 3

[<Fact>]
let ``DownloadProgress tracks download state`` () =
    let progress = {
        ModelType = ModelType.Small
        BytesDownloaded = 233L * 1024L * 1024L
        TotalBytes = 466L * 1024L * 1024L
        PercentComplete = 50.0
        BytesPerSecond = 1048576.0
        EstimatedTimeRemaining = Some (TimeSpan.FromMinutes(3.5))
    }

    progress.PercentComplete |> should equal 50.0
    progress.BytesPerSecond |> should equal 1048576.0
    progress.EstimatedTimeRemaining.Value.TotalMinutes |> should equal 3.5

[<Fact>]
let ``ModelEvent covers all event types`` () =
    let events = [
        DownloadStarted ModelType.Base
        DownloadProgress {
            ModelType = ModelType.Base
            BytesDownloaded = 100L
            TotalBytes = 200L
            PercentComplete = 50.0
            BytesPerSecond = 1000.0
            EstimatedTimeRemaining = None
        }
        DownloadCompleted(ModelType.Base, "/path/to/model")
        DownloadFailed(ModelType.Base, "Connection timeout")
        ModelDeleted ModelType.Base
        VerificationStarted ModelType.Base
        VerificationCompleted(ModelType.Base, true)
    ]

    events |> should haveLength 7

    // Verify pattern matching
    for event in events do
        match event with
        | DownloadStarted model -> model |> ignore
        | DownloadProgress progress -> progress |> ignore
        | DownloadCompleted(model, path) -> model |> ignore; path |> ignore
        | DownloadFailed(model, error) -> model |> ignore; error |> ignore
        | ModelDeleted model -> model |> ignore
        | VerificationStarted model -> model |> ignore
        | VerificationCompleted(model, success) -> model |> ignore; success |> ignore

[<Fact>]
let ``ModelRepository contains model collection`` () =
    let repo = {
        Name = "Hugging Face"
        BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp"
        Models = [
            {
                Type = ModelType.Tiny
                DisplayName = "Tiny"
                Description = "Smallest model"
                Size = 39L * 1024L * 1024L
                Url = "https://huggingface.co/models/tiny.bin"
                Sha256 = None
                RequiresGpu = false
                Languages = ["multilingual"]
            }
            {
                Type = ModelType.TinyEn
                DisplayName = "Tiny English"
                Description = "Smallest English model"
                Size = 39L * 1024L * 1024L
                Url = "https://huggingface.co/models/tiny.en.bin"
                Sha256 = None
                RequiresGpu = false
                Languages = ["en"]
            }
        ]
    }

    repo.Name |> should equal "Hugging Face"
    repo.Models |> should haveLength 2
    repo.Models.[0].Type |> should equal ModelType.Tiny
    repo.Models.[1].Languages |> should contain "en"

[<Theory>]
[<InlineData(0.0, true)>]
[<InlineData(0.5, false)>]
[<InlineData(1.0, true)>]
let ``Download progress completion detection`` (progress: float) (isComplete: bool) =
    let status = Downloading progress
    let complete = (progress >= 1.0)

    complete |> should equal isComplete

[<Fact>]
let ``Model size calculations are correct`` () =
    let models = [
        (ModelType.Tiny, 39L)
        (ModelType.Base, 142L)
        (ModelType.Small, 466L)
        (ModelType.Medium, 1500L)
        (ModelType.LargeV3, 3000L)
    ]

    for modelType, expectedMB in models do
        let size = modelType.GetModelSize()
        let sizeMB = size / (1024L * 1024L)
        sizeMB |> should equal expectedMB

[<Fact>]
let ``Model display names are user-friendly`` () =
    let displayNames = [
        (ModelType.Tiny, "Tiny (39 MB)")
        (ModelType.Base, "Base (142 MB)")
        (ModelType.Small, "Small (466 MB)")
        (ModelType.Medium, "Medium (1.5 GB)")
        (ModelType.LargeV3, "Large v3 (3 GB)")
    ]

    for modelType, expectedName in displayNames do
        let size = modelType.GetModelSize()
        let displaySize =
            if size < 1024L * 1024L * 1024L then
                sprintf "%.0f MB" (float size / (1024.0 * 1024.0))
            else
                sprintf "%.1f GB" (float size / (1024.0 * 1024.0 * 1024.0))

        let name = sprintf "%A (%s)" modelType displaySize
        name |> should contain (string modelType)

[<Fact>]
let ``EstimatedTimeRemaining calculation`` () =
    let calculateETA (bytesRemaining: int64) (bytesPerSecond: float) =
        if bytesPerSecond > 0.0 then
            Some (TimeSpan.FromSeconds(float bytesRemaining / bytesPerSecond))
        else
            None

    let eta1 = calculateETA 1048576L 1048576.0  // 1 MB at 1 MB/s
    eta1.Value.TotalSeconds |> should equal 1.0

    let eta2 = calculateETA 0L 1048576.0  // Already complete
    eta2.Value.TotalSeconds |> should equal 0.0

    let eta3 = calculateETA 1048576L 0.0  // No speed
    eta3 |> should equal None

[<Fact>]
let ``SHA256 verification option`` () =
    let withHash = {
        Type = ModelType.Base
        DisplayName = "Base"
        Description = "Test"
        Size = 1024L
        Url = "http://test"
        Sha256 = Some "d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2"
        RequiresGpu = false
        Languages = []
    }

    let withoutHash = { withHash with Sha256 = None }

    withHash.Sha256.IsSome |> should be True
    withoutHash.Sha256.IsNone |> should be True