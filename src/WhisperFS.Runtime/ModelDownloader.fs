namespace WhisperFS.Runtime

open System
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Threading
open WhisperFS

/// Model downloading and management
module ModelDownloader =

    /// Hugging Face repository for GGML models
    let [<Literal>] HuggingFaceRepo = "ggerganov/whisper.cpp"
    let [<Literal>] HuggingFaceBaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main"

    /// Model metadata definitions
    let modelMetadata = [
        { Type = Tiny
          DisplayName = "Tiny (39 MB)"
          Description = "Fastest, least accurate. Good for quick drafts."
          Size = 39L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-tiny.bin"
          Sha256 = None
          RequiresGpu = false
          Languages = ["en"; "multilingual"] }

        { Type = TinyEn
          DisplayName = "Tiny English (39 MB)"
          Description = "Fastest English-only model."
          Size = 39L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-tiny.en.bin"
          Sha256 = None
          RequiresGpu = false
          Languages = ["en"] }

        { Type = Base
          DisplayName = "Base (142 MB)"
          Description = "Good balance of speed and accuracy."
          Size = 142L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-base.bin"
          Sha256 = None
          RequiresGpu = false
          Languages = ["en"; "multilingual"] }

        { Type = BaseEn
          DisplayName = "Base English (142 MB)"
          Description = "English-optimized base model."
          Size = 142L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-base.en.bin"
          Sha256 = None
          RequiresGpu = false
          Languages = ["en"] }

        { Type = Small
          DisplayName = "Small (466 MB)"
          Description = "Good accuracy, reasonable speed."
          Size = 466L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-small.bin"
          Sha256 = None
          RequiresGpu = false
          Languages = ["en"; "multilingual"] }

        { Type = SmallEn
          DisplayName = "Small English (466 MB)"
          Description = "English-optimized small model."
          Size = 466L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-small.en.bin"
          Sha256 = None
          RequiresGpu = false
          Languages = ["en"] }

        { Type = Medium
          DisplayName = "Medium (1.5 GB)"
          Description = "High accuracy, slower processing."
          Size = 1500L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-medium.bin"
          Sha256 = None
          RequiresGpu = false
          Languages = ["en"; "multilingual"] }

        { Type = MediumEn
          DisplayName = "Medium English (1.5 GB)"
          Description = "English-optimized medium model."
          Size = 1500L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-medium.en.bin"
          Sha256 = None
          RequiresGpu = false
          Languages = ["en"] }

        { Type = LargeV1
          DisplayName = "Large v1 (3 GB)"
          Description = "Original large model. Very high accuracy."
          Size = 3000L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-large-v1.bin"
          Sha256 = None
          RequiresGpu = true  // Recommended
          Languages = ["en"; "multilingual"] }

        { Type = LargeV2
          DisplayName = "Large v2 (3 GB)"
          Description = "Improved large model with better accuracy."
          Size = 3000L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-large-v2.bin"
          Sha256 = None
          RequiresGpu = true
          Languages = ["en"; "multilingual"] }

        { Type = LargeV3
          DisplayName = "Large v3 (3 GB)"
          Description = "Latest and most accurate model."
          Size = 3000L * 1024L * 1024L
          Url = $"{HuggingFaceBaseUrl}/ggml-large-v3.bin"
          Sha256 = None
          RequiresGpu = true
          Languages = ["en"; "multilingual"] }
    ]

    /// Model status tracking
    let private modelStatus = System.Collections.Concurrent.ConcurrentDictionary<ModelType, ModelStatus>()

    /// Event for model management updates
    let private modelEvents = Event<ModelEvent>()

    /// Public event observable
    let ModelEvents = modelEvents.Publish

    /// Get the models directory
    let getModelsDirectory() =
        let baseDir =
            match Environment.GetEnvironmentVariable("WHISPERFS_MODELS_DIR") with
            | null | "" ->
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WhisperFS",
                    "models")
            | dir -> dir
        Directory.CreateDirectory(baseDir) |> ignore
        baseDir

    /// Get path for a specific model
    let getModelPath (modelType: ModelType) =
        let modelsDir = getModelsDirectory()
        let fileName = modelType.GetModelName() + ".bin"
        Path.Combine(modelsDir, fileName)

    /// Check if a model is downloaded
    let isModelDownloaded (modelType: ModelType) =
        let path = getModelPath modelType
        if File.Exists(path) then
            let fileInfo = FileInfo(path)
            let expectedSize =
                modelMetadata
                |> List.tryFind (fun m -> m.Type = modelType)
                |> Option.map (fun m -> m.Size)
                |> Option.defaultValue 0L

            // Check if file size is reasonable (within 5% of expected)
            if expectedSize > 0L then
                let sizeDiff = abs(fileInfo.Length - expectedSize)
                let tolerance = float expectedSize * 0.05
                fileInfo.Length > 0L && float sizeDiff < tolerance
            else
                fileInfo.Length > 0L
        else
            false

    /// Get current status of a model
    let getModelStatus (modelType: ModelType) =
        match modelStatus.TryGetValue(modelType) with
        | true, status -> status
        | false, _ ->
            if isModelDownloaded modelType then
                let path = getModelPath modelType
                let size = (FileInfo(path)).Length
                let status = Downloaded(path, size)
                modelStatus.TryAdd(modelType, status) |> ignore
                status
            else
                NotDownloaded

    /// Get status of all models
    let getAllModelStatus() =
        modelMetadata
        |> List.map (fun meta ->
            let status = getModelStatus meta.Type
            (meta, status))

    /// Download a model with progress reporting
    let downloadModelAsync (modelType: ModelType) (cancellationToken: CancellationToken) = async {
        let metadata =
            modelMetadata
            |> List.tryFind (fun m -> m.Type = modelType)

        match metadata with
        | None ->
            return Error (WhisperError.ModelLoadError $"Unknown model type: {modelType}")
        | Some meta ->
            try
                // Check if already downloaded
                if isModelDownloaded modelType then
                    let path = getModelPath modelType
                    let size = (FileInfo(path)).Length
                    modelStatus.AddOrUpdate(modelType, Downloaded(path, size), fun _ _ -> Downloaded(path, size)) |> ignore
                    return Ok path
                else
                    // Start download
                    modelEvents.Trigger(DownloadStarted modelType)
                    modelStatus.AddOrUpdate(modelType, Downloading 0.0, fun _ _ -> Downloading 0.0) |> ignore

                    use client = new HttpClient()
                    client.DefaultRequestHeaders.Add("User-Agent", "WhisperFS/1.0")
                    client.Timeout <- TimeSpan.FromHours(2.0)  // Large files need time

                    // Get response with content length
                    let! response =
                        client.GetAsync(meta.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        |> Async.AwaitTask

                    response.EnsureSuccessStatusCode() |> ignore

                    let totalBytes =
                        response.Content.Headers.ContentLength
                        |> Option.ofNullable
                        |> Option.defaultValue meta.Size

                    let modelPath = getModelPath modelType
                    let tempPath = modelPath + ".tmp"

                    // Download with progress
                    use stream = response.Content.ReadAsStreamAsync() |> Async.AwaitTask |> Async.RunSynchronously
                    use fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true)

                    let buffer = Array.zeroCreate<byte> (8192)
                    let mutable bytesRead = 0
                    let mutable totalRead = 0L
                    let startTime = DateTime.UtcNow
                    let mutable lastProgressTime = DateTime.UtcNow

                    let rec downloadLoop() = async {
                        let! read = stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken) |> Async.AwaitTask
                        if read > 0 then
                            do! fileStream.WriteAsync(buffer, 0, read, cancellationToken) |> Async.AwaitTask
                            totalRead <- totalRead + int64 read

                            // Update progress every 100ms
                            let now = DateTime.UtcNow
                            if (now - lastProgressTime).TotalMilliseconds > 100.0 then
                                lastProgressTime <- now
                                let progress = float totalRead / float totalBytes
                                let elapsed = now - startTime
                                let bytesPerSecond =
                                    if elapsed.TotalSeconds > 0.0 then
                                        float totalRead / elapsed.TotalSeconds
                                    else
                                        0.0

                                let remaining =
                                    if bytesPerSecond > 0.0 then
                                        let remainingBytes = totalBytes - totalRead
                                        Some (TimeSpan.FromSeconds(float remainingBytes / bytesPerSecond))
                                    else
                                        None

                                modelStatus.AddOrUpdate(modelType, Downloading progress, fun _ _ -> Downloading progress) |> ignore

                                modelEvents.Trigger(DownloadProgress {
                                    ModelType = modelType
                                    BytesDownloaded = totalRead
                                    TotalBytes = totalBytes
                                    PercentComplete = progress * 100.0
                                    BytesPerSecond = bytesPerSecond
                                    EstimatedTimeRemaining = remaining
                                })

                            return! downloadLoop()
                    }

                    do! downloadLoop()
                    do! fileStream.FlushAsync() |> Async.AwaitTask
                    fileStream.Close()

                    // Verify if needed
                    let! verificationResult = async {
                        match meta.Sha256 with
                        | Some expectedHash ->
                            modelEvents.Trigger(VerificationStarted modelType)
                            use sha256 = SHA256.Create()
                            use verifyStream = File.OpenRead(tempPath)
                            let! hashBytes = sha256.ComputeHashAsync(verifyStream, cancellationToken) |> Async.AwaitTask
                            let actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()

                            if actualHash = expectedHash.ToLowerInvariant() then
                                modelEvents.Trigger(VerificationCompleted(modelType, true))
                                return Ok ()
                            else
                                File.Delete(tempPath)
                                let error = "Model verification failed: hash mismatch"
                                modelStatus.AddOrUpdate(modelType, Failed error, fun _ _ -> Failed error) |> ignore
                                modelEvents.Trigger(VerificationCompleted(modelType, false))
                                return Error (WhisperError.ModelLoadError error)
                        | None ->
                            return Ok ()
                    }

                    match verificationResult with
                    | Error e ->
                        return Error e
                    | Ok () ->
                        // Move to final location
                        File.Move(tempPath, modelPath, true)

                        let finalSize = (FileInfo(modelPath)).Length
                        modelStatus.AddOrUpdate(modelType, Downloaded(modelPath, finalSize), fun _ _ -> Downloaded(modelPath, finalSize)) |> ignore
                        modelEvents.Trigger(DownloadCompleted(modelType, modelPath))

                        return Ok modelPath

            with
            | :? OperationCanceledException ->
                modelStatus.AddOrUpdate(modelType, NotDownloaded, fun _ _ -> NotDownloaded) |> ignore
                return Error WhisperError.Cancelled
            | ex ->
                let error = ex.Message
                modelStatus.AddOrUpdate(modelType, Failed error, fun _ _ -> Failed error) |> ignore
                modelEvents.Trigger(DownloadFailed(modelType, error))
                return Error (WhisperError.NetworkError error)
    }

    /// Delete a downloaded model
    let deleteModel (modelType: ModelType) =
        try
            let path = getModelPath modelType
            if File.Exists(path) then
                File.Delete(path)
                modelStatus.AddOrUpdate(modelType, NotDownloaded, fun _ _ -> NotDownloaded) |> ignore
                modelEvents.Trigger(ModelDeleted modelType)
                Ok ()
            else
                Error (WhisperError.FileNotFound path)
        with ex ->
            Error (WhisperError.ProcessingError(0, ex.Message))

    /// Get total size of downloaded models
    let getTotalDownloadedSize() =
        modelMetadata
        |> List.sumBy (fun meta ->
            if isModelDownloaded meta.Type then
                let path = getModelPath meta.Type
                (FileInfo(path)).Length
            else
                0L)

    /// Get available disk space
    let getAvailableDiskSpace() =
        let modelsDir = getModelsDirectory()
        let drive = DriveInfo(Path.GetPathRoot(modelsDir))
        drive.AvailableFreeSpace

    /// Estimate download time
    let estimateDownloadTime (modelType: ModelType) (bytesPerSecond: float) =
        let metadata = modelMetadata |> List.tryFind (fun m -> m.Type = modelType)
        match metadata with
        | Some meta when bytesPerSecond > 0.0 ->
            Some (TimeSpan.FromSeconds(float meta.Size / bytesPerSecond))
        | _ -> None