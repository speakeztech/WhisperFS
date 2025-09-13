module WhisperFS.Tests.ConfigurationTests

open System
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.TestHelpers

[<Fact>]
let ``Default configuration has valid values`` () =
    let config = WhisperConfig.defaultConfig

    config.ThreadCount |> should be (greaterThan 0)
    config.MaxTextContext |> should be (greaterThan 0)
    config.Strategy |> should equal SamplingStrategy.Greedy
    config.Language |> should equal None
    config.Temperature |> should equal 0.0f

[<Theory>]
[<InlineData(1)>]
[<InlineData(4)>]
[<InlineData(8)>]
let ``Thread count validation accepts valid values`` (threadCount: int) =
    let config = { WhisperConfig.defaultConfig with ThreadCount = threadCount }
    config.ThreadCount |> should equal threadCount
    config.ThreadCount |> should be (greaterThan 0)

[<Theory>]
[<InlineData(1024)>]
[<InlineData(8192)>]
[<InlineData(16384)>]
let ``Max text context validation accepts valid values`` (maxContext: int) =
    let config = { WhisperConfig.defaultConfig with MaxTextContext = maxContext }
    config.MaxTextContext |> should equal maxContext
    config.MaxTextContext |> should be (greaterThan 0)

[<Theory>]
[<InlineData(0.0f)>]
[<InlineData(0.5f)>]
[<InlineData(1.0f)>]
let ``Temperature validation accepts valid range`` (temperature: float32) =
    let config = { WhisperConfig.defaultConfig with Temperature = temperature }
    config.Temperature |> should equal temperature
    config.Temperature |> should be (greaterThanOrEqualTo 0.0f)
    config.Temperature |> should be (lessThanOrEqualTo 1.0f)

[<Fact>]
let ``Configuration with custom model path`` () =
    let customPath = "/custom/model.bin"
    let config = { WhisperConfig.defaultConfig with ModelPath = customPath }

    config.ModelPath |> should equal customPath

[<Theory>]
[<InlineData("en")>]
[<InlineData("es")>]
[<InlineData("fr")>]
let ``Language configuration accepts valid codes`` (langCode: string) =
    let config = { WhisperConfig.defaultConfig with Language = Some langCode }

    config.Language |> should equal (Some langCode)

[<Fact>]
let ``Boolean flags can be toggled`` () =
    let config = {
        WhisperConfig.defaultConfig with
            Translate = true
            NoTimestamps = true
            PrintProgress = true
    }

    config.Translate |> should be True
    config.NoTimestamps |> should be True
    config.PrintProgress |> should be True

[<Fact>]
let ``Threshold values are within expected ranges`` () =
    let config = WhisperConfig.defaultConfig

    config.ThresholdPt |> should be (greaterThanOrEqualTo 0.0f)
    config.ThresholdPt |> should be (lessThanOrEqualTo 1.0f)
    config.ThresholdPtSum |> should be (greaterThanOrEqualTo 0.0f)
    config.NoSpeechThreshold |> should be (greaterThanOrEqualTo 0.0f)
    config.NoSpeechThreshold |> should be (lessThanOrEqualTo 1.0f)

[<Fact>]
let ``Sampling strategies are distinct`` () =
    let greedy = SamplingStrategy.Greedy
    let beam = SamplingStrategy.BeamSearch

    greedy |> should not' (equal beam)

[<Fact>]
let ``Model types cover expected variants`` () =
    let allTypes = TestData.allModelTypes

    allTypes |> should contain ModelType.Tiny
    allTypes |> should contain ModelType.Base
    allTypes |> should contain ModelType.Small
    allTypes |> should contain ModelType.Medium
    allTypes |> should contain ModelType.LargeV3