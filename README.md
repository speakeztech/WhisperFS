# WhisperFS

A comprehensive F# library providing streaming-capable bindings to [whisper.cpp](https://github.com/ggerganov/whisper.cpp), designed from the ground up to support both real-time transcription and batch processing scenarios.

## Features

- 🎯 **Complete Feature Parity with Whisper.NET** - Drop-in replacement with enhanced capabilities
- 🚀 **True Streaming Support** - Real-time transcription using whisper.cpp's state management
- 🔧 **Unified API** - Single `IWhisperClient` interface for both batch and streaming modes
- 📊 **Token-Level Access** - Confidence scores and timestamps for fine-grained control
- 🌍 **Language Detection** - Automatic language identification with confidence scores
- 💪 **Platform Optimized** - Automatic runtime selection (CUDA, CoreML, AVX, etc.)
- 🦀 **F# Idiomatic** - Leverages discriminated unions, async workflows, and observables
- ⚡ **Zero-Copy Operations** - Efficient memory management for audio buffers
- 🔄 **Robust Error Handling** - Result types with comprehensive error discrimination

## Installation

```bash
dotnet add package WhisperFS.Core
dotnet add package WhisperFS.Runtime
```

### Native Runtime Dependencies

WhisperFS automatically downloads and manages the appropriate native runtime for your platform:

- **Windows**: CUDA 12.0, AVX2, AVX, or NoAVX variants
- **macOS**: CoreML optimized or CPU variants
- **Linux**: CUDA, OpenVINO, or CPU variants

## Quick Start

### Batch Transcription (PTT Mode)

```fsharp
open WhisperFS

// Build a client with fluent configuration
let! clientResult =
    WhisperBuilder()
        .WithModel(ModelType.Base)
        .WithLanguage("en")
        .WithGpu()
        .Build()

match clientResult with
| Ok client ->
    // Process audio file
    let! result = client.ProcessFileAsync("audio.wav")

    match result with
    | Ok transcription ->
        printfn "Text: %s" transcription.FullText
        printfn "Duration: %A" transcription.Duration

        // Access segments with timestamps
        for segment in transcription.Segments do
            printfn "[%.2f-%.2f] %s"
                segment.StartTime
                segment.EndTime
                segment.Text
    | Error err ->
        printfn "Transcription failed: %A" err

| Error err ->
    printfn "Failed to create client: %A" err
```

### Streaming Transcription

```fsharp
open WhisperFS
open System.Reactive.Linq

// Create streaming client
let! clientResult =
    WhisperBuilder()
        .WithModel(ModelType.Base)
        .WithStreaming(chunkMs = 1000, overlapMs = 200)
        .WithTokenTimestamps()
        .Build()

match clientResult with
| Ok client ->
    // Create audio source (e.g., from microphone)
    let audioSource = AudioCapture.CreateMicrophone(sampleRate = 16000)

    // Process stream with real-time updates
    client.ProcessStream(audioSource)
    |> Observable.subscribe (function
        | PartialTranscription(text, tokens, confidence) ->
            printfn "Partial: %s (confidence: %.2f)" text confidence

        | FinalTranscription(text, tokens, segments) ->
            printfn "Final: %s" text

        | ProcessingError msg ->
            printfn "Error: %s" msg

        | _ -> ())
    |> ignore

| Error err ->
    printfn "Failed to create streaming client: %A" err
```

### Language Detection

```fsharp
let! detection = client.DetectLanguageAsync(audioSamples)

match detection with
| Ok lang ->
    printfn "Detected language: %s (confidence: %.2f)"
        lang.Language
        lang.Confidence

    // Access probabilities for all languages
    for KeyValue(language, probability) in lang.Probabilities do
        if probability > 0.01f then
            printfn "  %s: %.2f%%" language (probability * 100.0f)

| Error err ->
    printfn "Language detection failed: %A" err
```

### Advanced Configuration

```fsharp
let! client =
    WhisperBuilder()
        .WithModel(ModelType.LargeV3)
        .WithLanguageDetection()           // Auto-detect language
        .WithBeamSearch(beamSize = 5)      // Better accuracy
        .WithTemperature(0.0f)              // Deterministic output
        .WithPrompt("Technical terms: API, GPU, CPU, RAM")
        .WithTokenTimestamps()              // Enable token-level timestamps
        .WithMaxSegmentLength(30)           // Segment length in seconds
        .WithThreads(8)                     // Parallel processing
        .Build()
```

## API Reference

### Core Types

```fsharp
type TranscriptionEvent =
    | PartialTranscription of text:string * tokens:Token list * confidence:float32
    | FinalTranscription of text:string * tokens:Token list * segments:Segment list
    | ContextUpdate of contextData:byte[]
    | ProcessingError of error:string

type IWhisperClient =
    abstract member ProcessAsync: samples:float32[] -> Async<Result<TranscriptionResult, WhisperError>>
    abstract member ProcessStream: audioStream:IObservable<float32[]> -> IObservable<TranscriptionEvent>
    abstract member ProcessFileAsync: path:string -> Async<Result<TranscriptionResult, WhisperError>>
    abstract member DetectLanguageAsync: samples:float32[] -> Async<Result<LanguageDetection, WhisperError>>
    abstract member Reset: unit -> unit
    abstract member StreamingMode: bool with get, set
```

### Error Handling

```fsharp
type WhisperError =
    | ModelLoadError of message:string
    | ProcessingError of code:int * message:string
    | InvalidAudioFormat of message:string
    | StateError of message:string
    | NativeLibraryError of message:string
    | TokenizationError of message:string
    | OutOfMemory
    | Cancelled
```

## Migration from Whisper.NET

WhisperFS provides full backward compatibility with Whisper.NET through the `IWhisperProcessor` interface:

```fsharp
// Existing Whisper.NET code
let processor = whisperFactory.CreateBuilder()
    .WithLanguage("en")
    .Build()
let! result = processor.ProcessAsync(audioFile)

// WhisperFS - identical API
let processor = whisperFactory.CreateBuilder()
    .WithLanguage("en")
    .Build()
let! result = processor.ProcessAsync(audioFile)
```

### Enhanced Features Beyond Whisper.NET

| Feature | Whisper.NET | WhisperFS |
|---------|------------|-----------|
| Streaming | ❌ | ✅ Real-time with state management |
| Token Confidence | ❌ | ✅ Per-token probabilities |
| Language Detection | ❌ | ✅ With confidence scores |
| Custom Prompts | ❌ | ✅ Domain-specific vocabulary |
| Beam Search | ❌ | ✅ Configurable parameters |
| Error Handling | Exceptions | Result types |
| Observables | ❌ | ✅ Reactive extensions |

## Building from Source

```bash
# Clone the repository
git clone https://github.com/yourusername/WhisperFS.git
cd WhisperFS

# Build the solution
dotnet build

# Run tests
dotnet test

# Pack NuGet packages
dotnet pack -c Release
```

## Examples

See the [examples](examples/) directory for complete working examples:

- `BasicTranscription.fs` - Simple file transcription
- `StreamingMicrophone.fs` - Real-time microphone transcription
- `LanguageDetection.fs` - Multi-language detection
- `BatchProcessing.fs` - Processing multiple files
- `CustomVocabulary.fs` - Using domain-specific prompts

## Performance

WhisperFS is designed for optimal performance:

- **Memory Efficient**: Streaming processes audio in chunks, not loading entire files
- **Platform Optimized**: Automatically uses GPU acceleration when available
- **Parallel Processing**: Configurable thread count for CPU processing
- **Zero-Copy**: Direct memory access for native interop

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

MIT License - see [LICENSE](LICENSE) for details.

## Acknowledgments

- [whisper.cpp](https://github.com/ggerganov/whisper.cpp) - The underlying C++ implementation
- [Whisper.NET](https://github.com/sandrohanea/whisper.net) - Inspiration for .NET bindings
- [OpenAI Whisper](https://github.com/openai/whisper) - The original Whisper model

## Support

- 📖 [Documentation](https://github.com/yourusername/WhisperFS/wiki)
- 🐛 [Issue Tracker](https://github.com/yourusername/WhisperFS/issues)
- 💬 [Discussions](https://github.com/yourusername/WhisperFS/discussions)