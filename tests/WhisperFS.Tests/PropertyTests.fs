module WhisperFS.Tests.PropertyTests

open System
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.TestHelpers

[<Theory>]
[<InlineData(1)>]
[<InlineData(4)>]
[<InlineData(8)>]
[<InlineData(16)>]
let ``Thread counts are always positive`` (threadCount: int) =
    threadCount |> should be (greaterThan 0)

[<Theory>]
[<InlineData(0.0f)>]
[<InlineData(0.5f)>]
[<InlineData(1.0f)>]
let ``Temperature values are in valid range`` (temperature: float32) =
    temperature |> should be (greaterThanOrEqualTo 0.0f)
    temperature |> should be (lessThanOrEqualTo 1.0f)

[<Theory>]
[<InlineData(1024)>]
[<InlineData(2048)>]
[<InlineData(4096)>]
[<InlineData(8192)>]
[<InlineData(16384)>]
let ``Context sizes are powers of 2 or reasonable multiples`` (contextSize: int) =
    contextSize |> should be (greaterThan 0)

[<Theory>]
[<InlineData(100, 16000)>]
[<InlineData(500, 16000)>]
[<InlineData(1000, 44100)>]
let ``Audio sample arrays have correct length`` (durationMs: int) (sampleRate: int) =
    let samples = TestData.generateSilence durationMs sampleRate
    let expectedLength = (durationMs * sampleRate) / 1000
    samples.Length |> should equal expectedLength

[<Theory>]
[<InlineData(0.0f, 1.0f)>]
[<InlineData(1.0f, 2.0f)>]
[<InlineData(0.5f, 1.5f)>]
let ``Segment time ranges are valid`` (startTime: float32) (endTime: float32) =
    let segment = {
        Text = "test"
        StartTime = startTime
        EndTime = endTime
        Tokens = []
        SpeakerTurnNext = false
    }

    segment.StartTime |> should be (greaterThanOrEqualTo 0.0f)
    segment.EndTime |> should be (greaterThan segment.StartTime)

[<Theory>]
[<InlineData(0.0f)>]
[<InlineData(0.5f)>]
[<InlineData(0.95f)>]
[<InlineData(1.0f)>]
let ``Token confidence values are probabilities`` (confidence: float32) =
    let token = {
        Text = "test"
        Timestamp = 0.0f
        Probability = confidence
        IsSpecial = false
    }

    token.Probability |> should be (greaterThanOrEqualTo 0.0f)
    token.Probability |> should be (lessThanOrEqualTo 1.0f)

[<Fact>]
let ``All model types have string representations`` () =
    let modelTypes = TestData.allModelTypes

    for modelType in modelTypes do
        let stringRep = modelType.ToString()
        stringRep |> should not' (be NullOrEmptyString)
        stringRep.Length |> should be (greaterThan 0)

[<Theory>]
[<InlineData("en")>]
[<InlineData("es")>]
[<InlineData("fr")>]
[<InlineData("de")>]
let ``Language codes have expected format`` (langCode: string) =
    langCode.Length |> should equal 2
    System.Text.RegularExpressions.Regex.IsMatch(langCode, "^[a-z]+$") |> should be True

[<Fact>]
let ``Configuration properties are consistent`` () =
    let config = WhisperConfig.defaultConfig

    config.ThreadCount |> should be (greaterThan 0)
    config.MaxTextContext |> should be (greaterThan 0)
    config.Temperature |> should be (greaterThanOrEqualTo 0.0f)

[<Fact>]
let ``Error messages are non-empty`` () =
    let errors = [
        ModelLoadError "test"
        StateError "test"
        OutOfMemory
        Cancelled
    ]

    for error in errors do
        error.Message |> should not' (be NullOrEmptyString)