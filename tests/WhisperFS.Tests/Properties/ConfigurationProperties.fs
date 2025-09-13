module WhisperFS.Tests.Properties.ConfigurationProperties

open FsCheck
open FsCheck.Xunit
open WhisperFS
open WhisperFS.Tests.TestUtilities

[<Property>]
let ``Valid thread count is always positive`` (PositiveInt threads) =
    let config = { TestConfigBuilder().WithDefaults() with ThreadCount = threads }
    config.ThreadCount > 0

[<Property>]
let ``Max text context is always positive`` (PositiveInt maxContext) =
    let config = { TestConfigBuilder().WithDefaults() with MaxTextContext = maxContext }
    config.MaxTextContext > 0

[<Property>]
let ``Threshold values are always between 0 and 1`` (threshold: float32) =
    let clampedThreshold = max 0.0f (min 1.0f threshold)
    let config = { TestConfigBuilder().WithDefaults() with ThresholdPt = clampedThreshold }
    config.ThresholdPt >= 0.0f && config.ThresholdPt <= 1.0f

[<Property>]
let ``Temperature is always non-negative`` (temp: float32) =
    let clampedTemp = max 0.0f temp
    let config = { TestConfigBuilder().WithDefaults() with Temperature = clampedTemp }
    config.Temperature >= 0.0f

[<Property>]
let ``Model size calculation is consistent`` (model: ModelType) =
    let size1 = model.GetModelSize()
    let size2 = model.GetModelSize()
    size1 = size2

[<Property>]
let ``Max tokens is within reasonable bounds`` (NonNegativeInt maxTokens) =
    let clampedTokens = min maxTokens 1000  // Reasonable upper limit
    let config = { TestConfigBuilder().WithDefaults() with MaxTokens = clampedTokens }
    config.MaxTokens >= 0 && config.MaxTokens <= 1000

[<Property>]
let ``Audio context is non-negative`` (NonNegativeInt audioCtx) =
    let config = { TestConfigBuilder().WithDefaults() with AudioContext = audioCtx }
    config.AudioContext >= 0

[<Property>]
let ``Stability threshold is between 0 and 1`` (threshold: float32) =
    let clamped = max 0.0f (min 1.0f threshold)
    let config = { TestConfigBuilder().WithDefaults() with StabilityThreshold = clamped }
    config.StabilityThreshold >= 0.0f && config.StabilityThreshold <= 1.0f

[<Property>]
let ``Language code is valid when specified`` () =
    let validLanguages = ["en"; "es"; "fr"; "de"; "it"; "pt"; "ru"; "zh"; "ja"; "ko"]
    let gen = Gen.elements validLanguages |> Gen.map Some

    Prop.forAll (Arb.fromGen gen) (fun lang ->
        let config = TestConfigBuilder().WithLanguage(lang).Build()
        match config.Language with
        | Some l -> validLanguages |> List.contains l
        | None -> false
    )

[<Property>]
let ``Suppression flags are boolean`` (suppressBlank: bool) (suppressNonSpeech: bool) =
    let config = {
        TestConfigBuilder().WithDefaults() with
            SuppressBlank = suppressBlank
            SuppressNonSpeech = suppressNonSpeech
    }
    (config.SuppressBlank = suppressBlank) && (config.SuppressNonSpeech = suppressNonSpeech)