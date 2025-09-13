module WhisperFS.Tests.Mocks

open System
open System.IO
open System.Reactive.Subjects
open WhisperFS
open WhisperFS.Native
open WhisperFS.Runtime

/// Mock native context
type MockNativeContext() =
    let mutable isDisposed = false
    let mutable segments = []

    member _.AddSegment(segment: Segment) =
        segments <- segment :: segments

    member _.GetSegments() = segments |> List.rev

    member _.IsDisposed = isDisposed

    member _.Dispose() =
        isDisposed <- true

    interface IDisposable with
        member this.Dispose() = this.Dispose()

/// Mock WhisperClient for testing
type MockWhisperClient(config: WhisperConfig) =
    let events = Subject<Result<TranscriptionEvent, WhisperError>>()
    let mutable isDisposed = false
    let mutable resetCount = 0
    let mutable processedSamples = 0
    let metrics = {
        TotalProcessingTime = TimeSpan.Zero
        TotalAudioProcessed = TimeSpan.Zero
        AverageRealTimeFactor = 1.0
        SegmentsProcessed = 0
        TokensGenerated = 0
        ErrorCount = 0
    }

    /// Simulate processing delay
    let simulateProcessing (delayMs: int) =
        async {
            do! Async.Sleep delayMs
        }

    interface IWhisperClient with
        member _.ProcessAsync(samples: float32[]) =
            async {
                processedSamples <- processedSamples + samples.Length

                // Simulate processing delay
                do! simulateProcessing 50

                // Generate mock result
                let result = {
                    FullText = "Mock transcription"
                    Segments = [
                        {
                            Text = "Mock"
                            StartTime = 0.0f
                            EndTime = 0.5f
                            Tokens = []
                        }
                        {
                            Text = "transcription"
                            StartTime = 0.5f
                            EndTime = 1.0f
                            Tokens = []
                        }
                    ]
                    Duration = TimeSpan.FromSeconds(1.0)
                    ProcessingTime = TimeSpan.FromMilliseconds(50.0)
                    Timestamp = DateTime.UtcNow
                    Language = config.Language
                    LanguageConfidence = Some 0.95f
                    Tokens = None
                }

                return Ok result
            }

        member _.ProcessStream(audioStream: IObservable<float32[]>) =
            audioStream
            |> Observable.map (fun samples ->
                processedSamples <- processedSamples + samples.Length

                // Generate mock streaming event
                let event = TranscriptionEvent.PartialTranscription(
                    "Partial mock",
                    [],
                    0.85f
                )
                Ok event
            )

        member _.ProcessFileAsync(path: string) =
            async {
                if not (File.Exists(path)) then
                    return Error (FileNotFound path)
                else
                    // Simulate file processing
                    do! simulateProcessing 100

                    return Ok {
                        FullText = sprintf "Mock transcription from file: %s" (Path.GetFileName(path))
                        Segments = []
                        Duration = TimeSpan.FromSeconds(5.0)
                        ProcessingTime = TimeSpan.FromMilliseconds(100.0)
                        Timestamp = DateTime.UtcNow
                        Language = config.Language
                        LanguageConfidence = Some 0.92f
                        Tokens = None
                    }
            }

        member _.Process(input: WhisperInput) =
            match input with
            | BatchAudio samples ->
                BatchResult ((this :> IWhisperClient).ProcessAsync(samples))
            | StreamingAudio stream ->
                StreamingResult ((this :> IWhisperClient).ProcessStream(stream))
            | AudioFile path ->
                BatchResult ((this :> IWhisperClient).ProcessFileAsync(path))

        member _.Events = events :> IObservable<_>

        member _.Reset() =
            resetCount <- resetCount + 1
            processedSamples <- 0
            Ok ()

        member _.DetectLanguageAsync(samples: float32[]) =
            async {
                // Simulate language detection
                do! simulateProcessing 30

                return Ok {
                    Language = "en"
                    Confidence = 0.98f
                    Probabilities = Map.ofList [
                        ("en", 0.98f)
                        ("es", 0.01f)
                        ("fr", 0.01f)
                    ]
                }
            }

        member _.GetMetrics() = metrics

        member _.Dispose() =
            if not isDisposed then
                events.Dispose()
                isDisposed <- true

    member _.IsDisposed = isDisposed
    member _.ResetCount = resetCount
    member _.ProcessedSamples = processedSamples

    member _.SimulateError(error: WhisperError) =
        events.OnNext(Error error)

    member _.SimulateEvent(event: TranscriptionEvent) =
        events.OnNext(Ok event)

/// Mock model downloader
type MockModelDownloader() =
    let mutable downloads = Map.empty<ModelType, ModelStatus>
    let events = Subject<ModelEvent>()

    interface IModelDownloader with
        member _.DownloadModelAsync(modelType: ModelType) =
            async {
                // Simulate download start
                events.OnNext(DownloadStarted modelType)
                downloads <- downloads.Add(modelType, Downloading 0.0)

                // Simulate progress
                for i in 1 .. 10 do
                    do! Async.Sleep 10
                    let progress = float i / 10.0
                    downloads <- downloads.Add(modelType, Downloading progress)

                    events.OnNext(DownloadProgress {
                        ModelType = modelType
                        BytesDownloaded = int64 (progress * float (modelType.GetModelSize()))
                        TotalBytes = modelType.GetModelSize()
                        PercentComplete = progress * 100.0
                        BytesPerSecond = 1048576.0 // 1 MB/s
                        EstimatedTimeRemaining = Some (TimeSpan.FromSeconds(10.0 - float i))
                    })

                // Complete download
                let path = Path.Combine(Path.GetTempPath(), sprintf "%s.bin" (modelType.GetModelName()))
                downloads <- downloads.Add(modelType, Downloaded(path, modelType.GetModelSize()))
                events.OnNext(DownloadCompleted(modelType, path))

                return path
            }

        member _.GetModelPath(modelType: ModelType) =
            match downloads.TryFind(modelType) with
            | Some (Downloaded(path, _)) -> path
            | _ -> ""

        member _.IsModelDownloaded(modelType: ModelType) =
            match downloads.TryFind(modelType) with
            | Some (Downloaded _) -> true
            | _ -> false

        member _.GetDownloadProgress() =
            downloads
            |> Map.toList
            |> List.choose (fun (model, status) ->
                match status with
                | Downloading progress -> Some (model, progress)
                | _ -> None)
            |> List.tryHead
            |> Option.map snd
            |> Option.defaultValue 0.0

    member _.SimulateDownloadFailure(modelType: ModelType, error: string) =
        downloads <- downloads.Add(modelType, Failed error)
        events.OnNext(DownloadFailed(modelType, error))

    member _.GetStatus(modelType: ModelType) =
        downloads.TryFind(modelType) |> Option.defaultValue NotDownloaded

    member _.Events = events :> IObservable<_>

/// Mock whisper factory
type MockWhisperFactory() =
    let mutable clients = []
    let mutable isDisposed = false

    interface IWhisperFactory with
        member _.CreateClient(config: WhisperConfig) =
            let client = new MockWhisperClient(config)
            clients <- client :: clients
            client :> IWhisperClient

        member _.FromPath(modelPath: string) =
            if File.Exists(modelPath) then
                Ok (this :> IWhisperFactory)
            else
                Error (ModelLoadError (sprintf "Model file not found: %s" modelPath))

        member _.FromBuffer(buffer: byte[]) =
            if buffer.Length > 0 then
                Ok (this :> IWhisperFactory)
            else
                Error (ModelLoadError "Empty model buffer")

        member _.GetModelInfo() =
            {
                Type = ModelType.Base
                VocabSize = 51864
                AudioContext = 1500
                AudioState = 512
                Languages = ["en"; "es"; "fr"; "de"; "it"; "pt"; "ru"; "zh"; "ja"; "ko"]
            }

        member _.Dispose() =
            if not isDisposed then
                clients |> List.iter (fun c -> c.Dispose())
                isDisposed <- true

    member _.CreatedClients = clients
    member _.IsDisposed = isDisposed

/// Mock streaming source
type MockAudioStream() =
    let subject = Subject<float32[]>()

    member _.SendAudio(samples: float32[]) =
        subject.OnNext(samples)

    member _.Complete() =
        subject.OnCompleted()

    member _.Error(error: Exception) =
        subject.OnError(error)

    member _.AsObservable() =
        subject :> IObservable<_>

/// Mock performance metrics
type MockMetricsCollector() =
    let mutable metrics = []

    member _.RecordMetric(name: string, value: float) =
        metrics <- (name, value, DateTime.UtcNow) :: metrics

    member _.GetMetrics() = metrics |> List.rev

    member _.GetAverageMetric(name: string) =
        metrics
        |> List.filter (fun (n, _, _) -> n = name)
        |> List.averageBy (fun (_, v, _) -> v)

    member _.Clear() =
        metrics <- []

/// Test fixture with common setup
type TestFixture() =
    let tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())

    do
        Directory.CreateDirectory(tempDir) |> ignore

    member _.TempDirectory = tempDir

    member _.CreateTempFile(name: string) =
        Path.Combine(tempDir, name)

    member _.Cleanup() =
        if Directory.Exists(tempDir) then
            Directory.Delete(tempDir, true)

    interface IDisposable with
        member this.Dispose() = this.Cleanup()