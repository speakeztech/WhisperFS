namespace WhisperFS.Runtime

open System
open System.Threading
open WhisperFS

/// Public API for model management
module Models =

    /// Get metadata for all available models
    let getAvailableModels() =
        ModelDownloader.modelMetadata

    /// Get current status of all models (downloaded, not downloaded, etc.)
    let getAllModelStatus() =
        ModelDownloader.getAllModelStatus()

    /// Get models that are currently downloaded
    let getDownloadedModels() =
        ModelDownloader.getAllModelStatus()
        |> List.choose (fun (meta, status) ->
            match status with
            | Downloaded(path, size) -> Some (meta, path, size)
            | _ -> None)

    /// Get models that are not downloaded
    let getNotDownloadedModels() =
        ModelDownloader.getAllModelStatus()
        |> List.choose (fun (meta, status) ->
            match status with
            | NotDownloaded -> Some meta
            | _ -> None)

    /// Check if a specific model is downloaded
    let isModelDownloaded modelType =
        ModelDownloader.isModelDownloaded modelType

    /// Get the status of a specific model
    let getModelStatus modelType =
        ModelDownloader.getModelStatus modelType

    /// Get the path to a model (whether downloaded or not)
    let getModelPath modelType =
        ModelDownloader.getModelPath modelType

    /// Download a model with progress tracking
    let downloadModelAsync modelType =
        ModelDownloader.downloadModelAsync modelType CancellationToken.None

    /// Download a model with cancellation support
    let downloadModelWithCancellationAsync modelType cancellationToken =
        ModelDownloader.downloadModelAsync modelType cancellationToken

    /// Delete a downloaded model
    let deleteModel modelType =
        ModelDownloader.deleteModel modelType

    /// Subscribe to model management events
    let subscribeToModelEvents (handler: ModelEvent -> unit) =
        ModelDownloader.ModelEvents.Subscribe(handler)

    /// Get total size of all downloaded models
    let getTotalDownloadedSize() =
        ModelDownloader.getTotalDownloadedSize()

    /// Get available disk space for models
    let getAvailableDiskSpace() =
        ModelDownloader.getAvailableDiskSpace()

    /// Get recommended model based on system capabilities
    let getRecommendedModel() =
        // Check available memory
        let availableMemory = GC.GetTotalMemory(false)
        let availableMemoryGB = float availableMemory / (1024.0 * 1024.0 * 1024.0)

        // Check if GPU is available (simplified check)
        let hasGpu =
            Environment.GetEnvironmentVariable("CUDA_PATH") <> null ||
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX)

        // Recommend based on resources
        if hasGpu && availableMemoryGB > 6.0 then
            LargeV3  // Best quality with GPU
        elif availableMemoryGB > 4.0 then
            Medium   // Good balance
        elif availableMemoryGB > 2.0 then
            Small    // Reasonable quality
        elif availableMemoryGB > 1.0 then
            Base     // Basic quality
        else
            Tiny     // Minimum requirements

    /// Get a formatted status string for UI display
    let getModelStatusString modelType =
        match getModelStatus modelType with
        | NotDownloaded -> "Not Downloaded"
        | Downloading progress -> sprintf "Downloading... %.1f%%" (progress * 100.0)
        | Downloaded(_, size) -> sprintf "Downloaded (%.1f MB)" (float size / 1048576.0)
        | Failed error -> sprintf "Failed: %s" error

/// Model selection for UI
type ModelSelection = {
    Model: ModelMetadata
    Status: ModelStatus
    StatusText: string
    IsRecommended: bool
    CanDelete: bool
}

module ModelUI =

    /// Get model selections for UI display (e.g., system tray settings)
    let getModelSelectionsForUI() =
        let recommended = Models.getRecommendedModel()

        Models.getAllModelStatus()
        |> List.map (fun (meta, status) ->
            let canDelete =
                match status with
                | Downloaded _ -> true
                | _ -> false

            {
                Model = meta
                Status = status
                StatusText = Models.getModelStatusString meta.Type
                IsRecommended = (meta.Type = recommended)
                CanDelete = canDelete
            })

    /// Handle model selection from UI
    let selectModelAsync (selection: ModelSelection) = async {
        match selection.Status with
        | NotDownloaded | Failed _ ->
            // Download the model
            return! Models.downloadModelAsync selection.Model.Type
        | Downloaded(path, _) ->
            // Model already downloaded, return path
            return Ok path
        | Downloading _ ->
            // Already downloading
            return Error (WhisperError.ProcessingError(0, "Model is currently downloading"))
    }

    /// Format download progress for UI
    let formatDownloadProgress (progress: DownloadProgress) =
        let percent = sprintf "%.1f%%" progress.PercentComplete
        let speed =
            if progress.BytesPerSecond > 1048576.0 then
                sprintf "%.1f MB/s" (progress.BytesPerSecond / 1048576.0)
            else
                sprintf "%.1f KB/s" (progress.BytesPerSecond / 1024.0)

        let remaining =
            match progress.EstimatedTimeRemaining with
            | Some time when time.TotalMinutes < 1.0 ->
                sprintf "%d seconds" (int time.TotalSeconds)
            | Some time when time.TotalHours < 1.0 ->
                sprintf "%d minutes" (int time.TotalMinutes)
            | Some time ->
                sprintf "%.1f hours" time.TotalHours
            | None -> "calculating..."

        sprintf "%s - %s - %s remaining" percent speed remaining