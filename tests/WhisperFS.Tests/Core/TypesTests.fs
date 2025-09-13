module WhisperFS.Tests.Core.TypesTests

open System
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.TestUtilities

[<Fact>]
let ``ModelType GetModelSize returns correct sizes`` () =
    // Test each model type
    ModelType.Tiny.GetModelSize() |> should equal (39L * 1024L * 1024L)
    ModelType.TinyEn.GetModelSize() |> should equal (39L * 1024L * 1024L)
    ModelType.Base.GetModelSize() |> should equal (142L * 1024L * 1024L)
    ModelType.BaseEn.GetModelSize() |> should equal (142L * 1024L * 1024L)
    ModelType.Small.GetModelSize() |> should equal (466L * 1024L * 1024L)
    ModelType.SmallEn.GetModelSize() |> should equal (466L * 1024L * 1024L)
    ModelType.Medium.GetModelSize() |> should equal (1500L * 1024L * 1024L)
    ModelType.MediumEn.GetModelSize() |> should equal (1500L * 1024L * 1024L)
    ModelType.LargeV1.GetModelSize() |> should equal (3000L * 1024L * 1024L)
    ModelType.LargeV2.GetModelSize() |> should equal (3000L * 1024L * 1024L)
    ModelType.LargeV3.GetModelSize() |> should equal (3000L * 1024L * 1024L)
    (ModelType.Custom "path").GetModelSize() |> should equal 0L

[<Fact>]
let ``ModelType GetModelName returns correct names`` () =
    ModelType.Tiny.GetModelName() |> should equal "ggml-tiny"
    ModelType.TinyEn.GetModelName() |> should equal "ggml-tiny.en"
    ModelType.Base.GetModelName() |> should equal "ggml-base"
    ModelType.BaseEn.GetModelName() |> should equal "ggml-base.en"
    ModelType.Small.GetModelName() |> should equal "ggml-small"
    ModelType.SmallEn.GetModelName() |> should equal "ggml-small.en"
    ModelType.Medium.GetModelName() |> should equal "ggml-medium"
    ModelType.MediumEn.GetModelName() |> should equal "ggml-medium.en"
    ModelType.LargeV1.GetModelName() |> should equal "ggml-large-v1"
    ModelType.LargeV2.GetModelName() |> should equal "ggml-large-v2"
    ModelType.LargeV3.GetModelName() |> should equal "ggml-large-v3"
    (ModelType.Custom "/path/to/model.bin").GetModelName() |> should equal "/path/to/model.bin"

[<Fact>]
let ``Token IsSpecial correctly identifies special tokens`` () =
    let specialToken = {
        Text = "<|endoftext|>"
        Timestamp = 0.0f
        Probability = 1.0f
        IsSpecial = true
    }

    let normalToken = {
        Text = "hello"
        Timestamp = 0.5f
        Probability = 0.95f
        IsSpecial = false
    }

    specialToken.IsSpecial |> should be True
    normalToken.IsSpecial |> should be False

[<Fact>]
let ``Segment contains correct timing information`` () =
    let segment = {
        Text = "Test segment"
        StartTime = 1.5f
        EndTime = 3.0f
        Tokens = [
            { Text = "Test"; Timestamp = 1.5f; Probability = 0.95f; IsSpecial = false }
            { Text = "segment"; Timestamp = 2.2f; Probability = 0.92f; IsSpecial = false }
        ]
    }

    segment.StartTime |> should equal 1.5f
    segment.EndTime |> should equal 3.0f
    segment.EndTime - segment.StartTime |> should equal 1.5f
    segment.Tokens |> should haveLength 2

[<Theory>]
[<InlineData(0.95f, true)>]
[<InlineData(0.5f, false)>]
[<InlineData(0.3f, false)>]
let ``Token probability thresholds work correctly`` (probability: float32) (isHighConfidence: bool) =
    let token = {
        Text = "word"
        Timestamp = 1.0f
        Probability = probability
        IsSpecial = false
    }

    let threshold = 0.8f
    (token.Probability >= threshold) |> should equal isHighConfidence

[<Fact>]
let ``TranscriptionResult contains all required fields`` () =
    let result = TestData.generateTestTranscriptionResult()

    result.FullText |> should not' (be EmptyString)
    result.Segments |> should not' (be Empty)
    result.Duration |> should be (greaterThan TimeSpan.Zero)
    result.ProcessingTime |> should be (greaterThan TimeSpan.Zero)
    result.Language |> should equal (Some "en")
    result.LanguageConfidence |> should equal (Some 0.95f)

[<Fact>]
let ``TranscriptionEvent discriminated union works correctly`` () =
    let partialEvent = TranscriptionEvent.PartialTranscription("partial", [], 0.85f)
    let finalEvent = TranscriptionEvent.FinalTranscription("final", [], [])
    let errorEvent = TranscriptionEvent.ProcessingError("error")

    match partialEvent with
    | PartialTranscription(text, _, confidence) ->
        text |> should equal "partial"
        confidence |> should equal 0.85f
    | _ -> failwith "Wrong event type"

    match finalEvent with
    | FinalTranscription(text, _, _) ->
        text |> should equal "final"
    | _ -> failwith "Wrong event type"

    match errorEvent with
    | ProcessingError msg ->
        msg |> should equal "error"
    | _ -> failwith "Wrong event type"

[<Fact>]
let ``LanguageDetection contains probabilities map`` () =
    let detection = {
        Language = "en"
        Confidence = 0.98f
        Probabilities = Map.ofList [
            ("en", 0.98f)
            ("es", 0.01f)
            ("fr", 0.01f)
        ]
    }

    detection.Language |> should equal "en"
    detection.Confidence |> should equal 0.98f
    detection.Probabilities.Count |> should equal 3
    detection.Probabilities.["en"] |> should equal 0.98f

[<Fact>]
let ``WhisperConfig default values are sensible`` () =
    let config = TestConfigBuilder().WithDefaults()

    config.ThreadCount |> should be (greaterThan 0)
    config.ChunkSizeMs |> should be (greaterThan 0)
    config.MinConfidence |> should be (greaterThanOrEqualTo 0.0f)
    config.MinConfidence |> should be (lessThanOrEqualTo 1.0f)
    config.Temperature |> should be (greaterThanOrEqualTo 0.0f)
    config.SuppressBlank |> should be True
    config.SuppressNonSpeechTokens |> should be True

[<Theory>]
[<InlineData("en", true)>]
[<InlineData("es", true)>]
[<InlineData("", false)>]
let ``WhisperConfig language settings`` (lang: string) (hasLanguage: bool) =
    let config =
        if hasLanguage then
            TestConfigBuilder().WithLanguage(Some lang).Build()
        else
            TestConfigBuilder().WithLanguage(None).Build()

    match config.Language with
    | Some l when hasLanguage -> l |> should equal lang
    | None when not hasLanguage -> ()
    | _ -> failwith "Unexpected language configuration"

[<Fact>]
let ``PerformanceMetrics tracks processing statistics`` () =
    let metrics = {
        TotalProcessingTime = TimeSpan.FromSeconds(10.0)
        TotalAudioProcessed = TimeSpan.FromMinutes(5.0)
        AverageRealTimeFactor = 30.0
        SegmentsProcessed = 150
        TokensGenerated = 1200
        ErrorCount = 2
    }

    metrics.TotalProcessingTime.TotalSeconds |> should equal 10.0
    metrics.TotalAudioProcessed.TotalMinutes |> should equal 5.0
    metrics.AverageRealTimeFactor |> should equal 30.0
    metrics.SegmentsProcessed |> should equal 150
    metrics.TokensGenerated |> should equal 1200
    metrics.ErrorCount |> should equal 2

[<Fact>]
let ``ModelInfo contains required metadata`` () =
    let info = {
        Type = ModelType.Base
        VocabSize = 51864
        AudioContext = 1500
        AudioState = 512
        Languages = ["en"; "es"; "fr"]
    }

    info.Type |> should equal ModelType.Base
    info.VocabSize |> should equal 51864
    info.AudioContext |> should equal 1500
    info.AudioState |> should equal 512
    info.Languages |> should haveLength 3
    info.Languages |> should contain "en"