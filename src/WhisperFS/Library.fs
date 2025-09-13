namespace WhisperFS

open System
open System.Reactive.Linq

/// Main WhisperFS API entry point
module WhisperFS =

    /// Initialize WhisperFS with default settings
    let initialize() =
        // Initialize native library
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
            // First ensure the native library is initialized
            let! initResult = initialize()
            match initResult with
            | Error e -> return Error e
            | Ok () ->
                // Get or download the model
                let! modelResult = Runtime.Models.downloadModelAsync config.ModelType
                match modelResult with
                | Error e -> return Error e
                | Ok modelPath ->
                    // Update config with the model path
                    let configWithPath = { config with ModelPath = modelPath }
                    // Create the client with the model
                    return! createClientFromModel modelPath configWithPath
        }