namespace WhisperFS

open System
open System.IO
open System.Net.Http
open System.Threading.Tasks

/// VAD model management and detection
module VoiceActivityDetection =

    /// VAD model types supported by whisper.cpp
    type VadModel =
        | SileroVAD
        | Custom of path:string

        member this.GetModelName() =
            match this with
            | SileroVAD -> "silero_vad.onnx"
            | Custom path -> Path.GetFileName(path)

        member this.GetDownloadUrl() =
            match this with
            | SileroVAD ->
                "https://github.com/snakers4/silero-vad/raw/master/files/silero_vad.onnx"
            | Custom _ -> ""

    /// VAD model manager for downloading and caching models
    type VadModelManager() =
        let httpClient = new HttpClient()
        let modelsDirectory =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WhisperFS",
                "vad_models"
            )

        do
            if not (Directory.Exists(modelsDirectory)) then
                Directory.CreateDirectory(modelsDirectory) |> ignore

        /// Get the local path for a VAD model
        member _.GetModelPath(model: VadModel) =
            match model with
            | Custom path -> path
            | _ -> Path.Combine(modelsDirectory, model.GetModelName())

        /// Check if a VAD model is already downloaded
        member this.IsModelAvailable(model: VadModel) =
            match model with
            | Custom path -> File.Exists(path)
            | _ -> File.Exists(this.GetModelPath(model))

        /// Download a VAD model if not already present
        member this.DownloadModelAsync(model: VadModel) =
            async {
                let modelPath = this.GetModelPath(model)

                if this.IsModelAvailable(model) then
                    return Ok modelPath
                else
                    match model with
                    | Custom _ ->
                        return Error (FileNotFound $"Custom VAD model not found at: {modelPath}")
                    | _ ->
                        try
                            let url = model.GetDownloadUrl()
                            let! response = httpClient.GetAsync(url) |> Async.AwaitTask

                            if response.IsSuccessStatusCode then
                                let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
                                do! File.WriteAllBytesAsync(modelPath, bytes) |> Async.AwaitTask
                                return Ok modelPath
                            else
                                return Error (NetworkError $"Failed to download VAD model: {response.StatusCode}")
                        with
                        | ex -> return Error (NativeLibraryError $"Error downloading VAD model: {ex.Message}")
            }

        /// Clean up downloaded models
        member _.ClearCache() =
            if Directory.Exists(modelsDirectory) then
                Directory.GetFiles(modelsDirectory, "*.onnx")
                |> Array.iter File.Delete

        interface IDisposable with
            member _.Dispose() =
                httpClient.Dispose()

    /// Simple VAD implementation using energy-based detection
    /// This is a fallback when no VAD model is available
    type SimpleVoiceActivityDetector(energyThreshold: float32, silenceDuration: float32) =
        let mutable lastSpeechTime = DateTime.MinValue
        let mutable speechStartTime = DateTime.MinValue
        let mutable isSpeaking = false
        let mutable sensitivity = 0.5f

        /// Calculate the energy of audio samples
        let calculateEnergy (samples: float32[]) =
            samples
            |> Array.map (fun s -> s * s)
            |> Array.average

        new() = SimpleVoiceActivityDetector(0.01f, 0.5f)

        interface IVoiceActivityDetector with
            member _.ProcessFrame(samples: float32[]) =
                let energy = calculateEnergy samples
                let adjustedThreshold = energyThreshold * (1.0f - sensitivity + 0.5f)

                if energy > adjustedThreshold then
                    lastSpeechTime <- DateTime.UtcNow
                    if not isSpeaking then
                        speechStartTime <- DateTime.UtcNow
                        isSpeaking <- true
                        VadResult.SpeechStarted
                    else
                        VadResult.SpeechContinuing
                else
                    if isSpeaking then
                        let silenceTime = (DateTime.UtcNow - lastSpeechTime).TotalSeconds
                        if silenceTime > float silenceDuration then
                            isSpeaking <- false
                            let duration = lastSpeechTime - speechStartTime
                            VadResult.SpeechEnded(duration)
                        else
                            VadResult.SpeechContinuing
                    else
                        VadResult.Silence

            member _.Reset() =
                isSpeaking <- false
                lastSpeechTime <- DateTime.MinValue
                speechStartTime <- DateTime.MinValue

            member _.Sensitivity
                with get() = sensitivity
                and set(value) = sensitivity <- Math.Max(0.0f, Math.Min(1.0f, value))

    /// Factory for creating VAD instances
    let createVadDetector (config: WhisperConfig) =
        async {
            match config.EnableVAD, config.VADModelPath with
            | false, _ ->
                return Ok (new SimpleVoiceActivityDetector() :> IVoiceActivityDetector)
            | true, Some modelPath ->
                // When VAD is enabled with a model path, we pass it to whisper.cpp
                // For now, return simple detector as whisper.cpp handles the actual VAD
                return Ok (new SimpleVoiceActivityDetector() :> IVoiceActivityDetector)
            | true, None ->
                // Download default Silero VAD model
                use manager = new VadModelManager()
                let! modelResult = manager.DownloadModelAsync(SileroVAD)
                match modelResult with
                | Ok _ ->
                    // Model downloaded, whisper.cpp will use it
                    return Ok (new SimpleVoiceActivityDetector() :> IVoiceActivityDetector)
                | Error e ->
                    return Error e
        }