module WhisperFS.Tests.UtilsTests

open System
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Tests.TestHelpers

[<Fact>]
let ``Result map transforms Ok values correctly`` () =
    let input = Ok 5
    let result = input |> Result.map (fun x -> x * 2)

    match result with
    | Ok 10 -> ()
    | _ -> failwith "Expected Ok 10"

[<Fact>]
let ``Result map preserves Error values`` () =
    let input = Error "test error"
    let result = input |> Result.map (fun x -> x * 2)

    match result with
    | Error "test error" -> ()
    | _ -> failwith "Expected Error"

[<Fact>]
let ``Result bind chains operations correctly`` () =
    let divide x y =
        if y = 0 then Error "Division by zero"
        else Ok (x / y)

    let result = Ok 10 |> Result.bind (fun x -> divide x 2)

    match result with
    | Ok 5 -> ()
    | _ -> failwith "Expected Ok 5"

[<Fact>]
let ``Result bind propagates errors`` () =
    let divide x y =
        if y = 0 then Error "Division by zero"
        else Ok (x / y)

    let result = Ok 10 |> Result.bind (fun x -> divide x 0)

    match result with
    | Error "Division by zero" -> ()
    | _ -> failwith "Expected division by zero error"

[<Fact>]
let ``List operations preserve functional principles`` () =
    let numbers = [1; 2; 3; 4; 5]

    // Map operation
    let doubled = numbers |> List.map (fun x -> x * 2)
    doubled |> should equal [2; 4; 6; 8; 10]

    // Filter operation
    let evens = numbers |> List.filter (fun x -> x % 2 = 0)
    evens |> should equal [2; 4]

    // Fold operation
    let sum = numbers |> List.fold (+) 0
    sum |> should equal 15

[<Fact>]
let ``Array operations work with audio samples`` () =
    let samples = TestData.generateSilence 1000 16000

    // All samples should be zero (silence)
    samples |> Array.forall (fun sample -> sample = 0.0f) |> should be True

    // Length should match expected duration
    samples.Length |> should equal 16000

[<Fact>]
let ``String operations handle transcription text`` () =
    let segments = [
        "Hello"
        " world"
        " this"
        " is"
        " a"
        " test"
    ]

    let fullText = segments |> String.concat ""
    fullText |> should equal "Hello world this is a test"

    // Trim whitespace
    let trimmed = fullText.Trim()
    trimmed |> should equal "Hello world this is a test"

[<Fact>]
let ``TimeSpan operations are accurate`` () =
    let duration1 = TimeSpan.FromSeconds(1.5)
    let duration2 = TimeSpan.FromMilliseconds(500.0)

    let total = duration1 + duration2
    total.TotalSeconds |> should equal 2.0

[<Fact>]
let ``Option operations handle language detection`` () =
    let maybeLanguage = Some "en"
    let defaultLanguage = "auto"

    let language = maybeLanguage |> Option.defaultValue defaultLanguage
    language |> should equal "en"

    let noneLanguage = None
    let fallbackLanguage = noneLanguage |> Option.defaultValue defaultLanguage
    fallbackLanguage |> should equal "auto"

[<Fact>]
let ``Sequence operations process tokens efficiently`` () =
    let tokens = [
        { Text = "Hello"; Timestamp = 0.0f; Probability = 0.9f; IsSpecial = false }
        { Text = "<SOT>"; Timestamp = 0.0f; Probability = 1.0f; IsSpecial = true }
        { Text = "world"; Timestamp = 0.5f; Probability = 0.8f; IsSpecial = false }
        { Text = "<EOT>"; Timestamp = 1.0f; Probability = 1.0f; IsSpecial = true }
    ]

    // Filter out special tokens
    let textTokens = tokens |> List.filter (fun t -> not t.IsSpecial)
    textTokens.Length |> should equal 2

    // Calculate average confidence
    let avgConfidence = textTokens |> List.averageBy (fun t -> float t.Probability)
    avgConfidence |> should (equalWithin 0.01) 0.85

[<Fact>]
let ``Validation functions use Result type correctly`` () =
    let validatePositive x =
        if x > 0 then Ok x
        else Error "Must be positive"

    match validatePositive 5 with | Ok _ -> () | Error _ -> failwith "Expected Ok"
    match validatePositive -1 with | Error _ -> () | Ok _ -> failwith "Expected Error"

[<Fact>]
let ``Pure functions have no side effects`` () =
    let input = [1; 2; 3]

    // Map doesn't modify original
    let doubled = input |> List.map (fun x -> x * 2)
    input |> should equal [1; 2; 3] // Original unchanged
    doubled |> should equal [2; 4; 6]

    // Filter doesn't modify original
    let evens = input |> List.filter (fun x -> x % 2 = 0)
    input |> should equal [1; 2; 3] // Original unchanged
    evens |> should equal [2]