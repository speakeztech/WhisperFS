module WhisperFS.Tests.TypesTests

open System
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.TestHelpers

[<Fact>]
let ``ModelType ToString returns non-empty values`` () =
    let modelTypes = TestData.allModelTypes

    for modelType in modelTypes do
        let stringRep = modelType.ToString()
        stringRep |> should not' (be NullOrEmptyString)
        stringRep.Length |> should be (greaterThan 0)

[<Fact>]
let ``SamplingStrategy values are distinct`` () =
    let strategies = [
        SamplingStrategy.Greedy
        SamplingStrategy.BeamSearch(5, 1)
    ]

    // All strategies should be unique
    strategies
    |> List.distinct
    |> List.length
    |> should equal strategies.Length

[<Fact>]
let ``WhisperConfig default values are reasonable`` () =
    let config = TestData.defaultTestConfig

    config.ThreadCount |> should be (greaterThan 0)
    config.MaxTextContext |> should be (greaterThan 0)
    config.Temperature |> should be (greaterThanOrEqualTo 0.0f)
    config.OffsetMs |> should be (greaterThanOrEqualTo 0)
    config.DurationMs |> should be (greaterThanOrEqualTo 0)

[<Fact>]
let ``Segment properties have correct types`` () =
    let segment = {
        Text = "Hello world"
        StartTime = 0.0f
        EndTime = 1.0f
        Tokens = []
        SpeakerTurnNext = false
    }

    segment.Text |> should not' (be NullOrEmptyString)
    segment.StartTime |> should be (greaterThanOrEqualTo 0.0f)
    segment.EndTime |> should be (greaterThan segment.StartTime)
    segment.Tokens |> should be Empty

[<Fact>]
let ``Token properties are valid`` () =
    let token = {
        Text = "hello"
        Timestamp = 0.5f
        Probability = 0.95f
        IsSpecial = false
    }

    token.Text |> should not' (be NullOrEmptyString)
    token.Timestamp |> should be (greaterThanOrEqualTo 0.0f)
    token.Probability |> should be (greaterThanOrEqualTo 0.0f)
    token.Probability |> should be (lessThanOrEqualTo 1.0f)

[<Fact>]
let ``TranscriptionResult has required fields`` () =
    let result = TestData.testResult

    result.FullText |> should not' (be NullOrEmptyString)
    result.Duration.TotalSeconds |> should be (greaterThan 0.0)
    result.Segments |> should not' (be Empty)
    result.Language |> should not' (be None)

[<Theory>]
[<InlineData("en")>]
[<InlineData("es")>]
[<InlineData("fr")>]
[<InlineData("de")>]
let ``Language codes are valid format`` (languageCode: string) =
    languageCode.Length |> should equal 2
    System.Text.RegularExpressions.Regex.IsMatch(languageCode, "^[a-z]{2}$") |> should be True

[<Fact>]
let ``PerformanceMetrics calculations are consistent`` () =
    let metrics = {
        TotalProcessingTime = TimeSpan.FromSeconds(1.0)
        TotalAudioProcessed = TimeSpan.FromSeconds(10.0)
        AverageRealTimeFactor = 0.1
        SegmentsProcessed = 5
        TokensGenerated = 20
        ErrorCount = 0
    }

    // Real-time factor should match calculation
    let expectedRtf = metrics.TotalProcessingTime.TotalSeconds / metrics.TotalAudioProcessed.TotalSeconds
    metrics.AverageRealTimeFactor |> should (equalWithin 0.01) expectedRtf