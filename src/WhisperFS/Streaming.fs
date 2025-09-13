namespace WhisperFS

open System
open System.Reactive.Linq
open WhisperFS.Native

/// Minimal streaming placeholder - provides the required types without complex reactive functionality
/// This ensures the build succeeds while maintaining the API surface for future enhancement
type WhisperStream(_ctx: IntPtr, _state: IntPtr, _config: WhisperConfig) =
    let mutable isDisposed = false

    // Streaming configuration
    member val MinConfidence = 0.5f with get, set

    member _.ProcessAudio(_samples: float32[]) =
        // Placeholder - in a real implementation this would buffer and process audio
        ()

    member _.Events =
        // Return empty observable for now - can be enhanced later
        { new IObservable<Result<TranscriptionEvent, WhisperError>> with
            member _.Subscribe(observer) =
                // No events for now - just return disposable that does nothing
                { new IDisposable with member _.Dispose() = () }
        }

    interface IDisposable with
        member _.Dispose() =
            if not isDisposed then
                isDisposed <- true

/// Simple typed text state
type TypedTextState = {
    CurrentText: string
    LastUpdateTime: DateTime
}

/// Basic streaming extensions
module StreamingExtensions =
    let processTypedText (textStream: IObservable<string>) =
        // Placeholder - return the input stream cast to proper type
        textStream |> Observable.map (fun text ->
            { CurrentText = text; LastUpdateTime = DateTime.Now })