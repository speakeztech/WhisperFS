# Model Management

## Overview

WhisperFS provides comprehensive model management with on-demand downloading, progress tracking, and status monitoring. Models are downloaded from Hugging Face's official Whisper GGML repository.

## Available Models

| Model | Size | Languages | Description | GPU Recommended |
|-------|------|-----------|-------------|-----------------|
| **Tiny** | 39 MB | Multilingual | Fastest, least accurate | No |
| **Tiny English** | 39 MB | English only | Fastest English model | No |
| **Base** | 142 MB | Multilingual | Good balance | No |
| **Base English** | 142 MB | English only | Balanced English model | No |
| **Small** | 466 MB | Multilingual | Good accuracy | No |
| **Small English** | 466 MB | English only | Good English accuracy | No |
| **Medium** | 1.5 GB | Multilingual | High accuracy | No |
| **Medium English** | 1.5 GB | English only | High English accuracy | No |
| **Large v1** | 3 GB | Multilingual | Original large model | Yes |
| **Large v2** | 3 GB | Multilingual | Improved large model | Yes |
| **Large v3** | 3 GB | Multilingual | Latest, most accurate | Yes |

## API Usage

### Listing Models and Status

```fsharp
open WhisperFS.Runtime

// Get all available models with their current status
let modelStatuses = Models.getAllModelStatus()

for (metadata, status) in modelStatuses do
    printfn "%s: %s"
        metadata.DisplayName
        (Models.getModelStatusString metadata.Type)

// Output:
// Tiny (39 MB): Not Downloaded
// Base (142 MB): Downloaded (142.3 MB)
// Small (466 MB): Downloading... 45.2%
// Large v3 (3 GB): Not Downloaded
```

### Getting Downloaded Models

```fsharp
// List only downloaded models
let downloaded = Models.getDownloadedModels()

for (metadata, path, size) in downloaded do
    printfn "%s at %s (%.1f MB)"
        metadata.DisplayName
        path
        (float size / 1048576.0)

// Check if specific model is downloaded
if Models.isModelDownloaded ModelType.Base then
    printfn "Base model is ready to use"
```

### Downloading Models

```fsharp
// Download a model with progress tracking
let downloadTask = async {
    // Subscribe to progress events
    use subscription = Models.subscribeToModelEvents(function
        | DownloadStarted model ->
            printfn "Starting download of %A" model

        | DownloadProgress progress ->
            printfn "%s" (ModelUI.formatDownloadProgress progress)

        | DownloadCompleted(model, path) ->
            printfn "Downloaded %A to %s" model path

        | DownloadFailed(model, error) ->
            printfn "Failed to download %A: %s" model error

        | _ -> ())

    // Start download
    let! result = Models.downloadModelAsync ModelType.Base

    match result with
    | Ok path ->
        printfn "Model ready at: %s" path
    | Error err ->
        printfn "Download failed: %s" err.Message
}

Async.RunSynchronously downloadTask
```

### Cancellable Downloads

```fsharp
// Download with cancellation support
let cts = new CancellationTokenSource()

// Start download
let downloadTask =
    Models.downloadModelWithCancellationAsync
        ModelType.LargeV3
        cts.Token

// Cancel after 30 seconds
cts.CancelAfter(TimeSpan.FromSeconds(30.0))

match Async.RunSynchronously downloadTask with
| Ok path -> printfn "Downloaded to %s" path
| Error WhisperError.Cancelled -> printfn "Download cancelled"
| Error err -> printfn "Error: %s" err.Message
```

## UI Integration

### System Tray Settings

```fsharp
open WhisperFS.Runtime

// Get model selections for UI dropdown
let selections = ModelUI.getModelSelectionsForUI()

for selection in selections do
    let indicator =
        match selection.Status with
        | Downloaded _ -> "✓"
        | Downloading _ -> "↓"
        | Failed _ -> "✗"
        | NotDownloaded -> " "

    let recommended = if selection.IsRecommended then " (Recommended)" else ""

    printfn "[%s] %s%s - %s"
        indicator
        selection.Model.DisplayName
        recommended
        selection.StatusText
```

### Handling User Selection

```fsharp
// When user selects a model in settings
let handleModelSelection (selection: ModelSelection) = async {
    match! ModelUI.selectModelAsync selection with
    | Ok path ->
        // Update configuration to use this model
        updateConfig { config with ModelPath = path }
        showNotification "Model ready to use"

    | Error err ->
        showError err.Message
}
```

## Storage Management

### Location

Models are stored in:
- **Windows**: `%LOCALAPPDATA%\WhisperFS\models\`
- **macOS**: `~/Library/Application Support/WhisperFS/models/`
- **Linux**: `~/.local/share/WhisperFS/models/`

Override with environment variable:
```bash
set WHISPERFS_MODELS_DIR=D:\AI\Models
```

### Disk Space

```fsharp
// Check disk space before downloading
let availableSpace = Models.getAvailableDiskSpace()
let totalDownloaded = Models.getTotalDownloadedSize()

printfn "Models using: %.1f GB" (float totalDownloaded / 1073741824.0)
printfn "Space available: %.1f GB" (float availableSpace / 1073741824.0)

// Delete a model to free space
match Models.deleteModel ModelType.LargeV1 with
| Ok () -> printfn "Model deleted"
| Error err -> printfn "Failed to delete: %s" err.Message
```

## Automatic Model Selection

```fsharp
// Get recommended model based on system resources
let recommended = Models.getRecommendedModel()

printfn "Recommended model for your system: %A" recommended
// Considers: Available RAM, GPU presence, disk space
```

## Progress Tracking

### Real-time Progress Updates

```fsharp
// Subscribe to detailed progress
Models.subscribeToModelEvents(function
    | DownloadProgress progress ->
        printfn "Model: %A" progress.ModelType
        printfn "Progress: %.1f%%" progress.PercentComplete
        printfn "Downloaded: %.1f MB / %.1f MB"
            (float progress.BytesDownloaded / 1048576.0)
            (float progress.TotalBytes / 1048576.0)
        printfn "Speed: %.1f MB/s"
            (progress.BytesPerSecond / 1048576.0)

        match progress.EstimatedTimeRemaining with
        | Some time ->
            printfn "Time remaining: %s" (time.ToString(@"mm\:ss"))
        | None -> ()
    | _ -> ())
```

## Model Information

### Getting Model Details

```fsharp
// Get metadata for all models
let models = Models.getAvailableModels()

for model in models do
    printfn "\n%s" model.DisplayName
    printfn "  Description: %s" model.Description
    printfn "  Size: %.1f MB" (float model.Size / 1048576.0)
    printfn "  Languages: %s" (String.Join(", ", model.Languages))
    printfn "  GPU Required: %b" model.RequiresGpu
```

## Best Practices

1. **Check Before Download**: Always check if model is already downloaded
2. **Handle Interruptions**: Downloads resume from where they left off
3. **Monitor Progress**: Subscribe to events for user feedback
4. **Respect Resources**: Use recommended model for best performance
5. **Clean Up**: Delete unused models to save disk space

## Error Handling

```fsharp
// Comprehensive error handling
let downloadWithRetry modelType maxRetries = async {
    let rec tryDownload attempt = async {
        match! Models.downloadModelAsync modelType with
        | Ok path -> return Ok path
        | Error err when attempt < maxRetries ->
            printfn "Attempt %d failed: %s. Retrying..."
                attempt err.Message
            do! Async.Sleep 5000
            return! tryDownload (attempt + 1)
        | Error err -> return Error err
    }

    return! tryDownload 1
}
```

## Performance Considerations

### Model Performance Guide

| Model | Speed | Accuracy | RAM Usage | Use Case |
|-------|-------|----------|-----------|----------|
| Tiny | 10x realtime | 60% | 200 MB | Quick drafts, real-time |
| Base | 7x realtime | 75% | 500 MB | General use |
| Small | 4x realtime | 85% | 1 GB | Professional |
| Medium | 2x realtime | 90% | 2.5 GB | High accuracy |
| Large | 1x realtime | 95% | 5 GB | Maximum accuracy |

## Integration Example

```fsharp
// Complete integration example
type TranscriptionService(config: WhisperConfig) =

    let ensureModel() = async {
        // Check if configured model is available
        if not (Models.isModelDownloaded config.ModelType) then
            printfn "Downloading required model: %A" config.ModelType

            // Download with progress
            match! Models.downloadModelAsync config.ModelType with
            | Ok path ->
                printfn "Model ready at: %s" path
                return Ok path
            | Error err ->
                // Fall back to smaller model
                printfn "Failed to download %A, trying Base model"
                    config.ModelType
                return! Models.downloadModelAsync ModelType.Base
        else
            return Ok (Models.getModelPath config.ModelType)
    }

    member _.InitializeAsync() = async {
        match! ensureModel() with
        | Ok modelPath ->
            // Initialize WhisperFS with model
            return WhisperFactory.FromPath(modelPath)
        | Error err ->
            return Error err
    }
```