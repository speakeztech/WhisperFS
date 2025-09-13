namespace WhisperFS

open System
open System.Threading
open System.Reactive.Linq

/// Main WhisperFS API entry point
module WhisperFS =

    /// Initialize WhisperFS with default settings
    let initialize() =
        async {
            let! nativeResult = Native.Library.initializeAsync()
            match nativeResult with
            | Ok runtime ->
                printfn "WhisperFS initialized with runtime: %A" runtime
                return Ok()
            | Error err ->
                return Error err
        }

    /// Create a new whisper client from a model path
    let createClientFromModel (modelPath: string) (config: WhisperConfig) =
        async {
            match! WhisperClientFactory.createFromPath modelPath config with
            | Ok client -> return Ok client
            | Error e -> return Error e
        }

    /// Create a new whisper client with automatic model download
    let createClient (config: WhisperConfig) =
        async {
            let! initResult = initialize()
            match initResult with
            | Error e -> return Error e
            | Ok () ->
                let! modelResult = Runtime.Models.downloadModelAsync config.ModelType
                match modelResult with
                | Error e -> return Error e
                | Ok modelPath ->
                    let configWithPath = { config with ModelPath = modelPath }
                    return! createClientFromModel modelPath configWithPath
        }

    /// Create a whisper client with custom context parameters
    let createClientWithParams (modelPath: string) (contextParams: WhisperFS.Native.WhisperContextParams) (config: WhisperConfig) =
        WhisperClientFactory.createWithParams modelPath contextParams config

    /// Download a model if not already present
    let downloadModel (modelType: ModelType) =
        Runtime.Models.downloadModelAsync modelType

    /// Check if a model is already downloaded
    let isModelDownloaded (modelType: ModelType) =
        Runtime.Models.isModelDownloaded modelType

    /// Get the local path for a model
    let getModelPath (modelType: ModelType) =
        Runtime.Models.getModelPath modelType