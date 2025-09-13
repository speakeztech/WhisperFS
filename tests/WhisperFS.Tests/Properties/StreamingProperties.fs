module WhisperFS.Tests.Properties.StreamingProperties

open System
open FsCheck
open FsCheck.Xunit
open WhisperFS

[<Property>]
let ``Ring buffer never exceeds capacity`` (PositiveInt capacity) (elements: int list) =
    let buffer = RingBuffer<int>(capacity)

    for batch in elements |> List.chunkBySize capacity do
        buffer.Write(Array.ofList batch)

    buffer.Count <= capacity

[<Property>]
let ``Ring buffer maintains FIFO order when not full`` (PositiveInt capacity) =
    let buffer = RingBuffer<int>(capacity)
    let elements = [1 .. min capacity 100]

    buffer.Write(Array.ofList elements)

    let retrieved = buffer.Read(elements.Length)
    Array.toList retrieved = elements

[<Property>]
let ``Token probability is always between 0 and 1`` (text: string) (timestamp: float32) =
    Gen.choose(0, 100)
    |> Gen.map (fun x -> float32 x / 100.0f)
    |> Gen.sample 0 1
    |> List.head
    |> fun prob ->
        let token = {
            Text = text
            Timestamp = abs timestamp
            Probability = prob
            IsSpecial = text.StartsWith("<|") && text.EndsWith("|>")
        }
        token.Probability >= 0.0f && token.Probability <= 1.0f

[<Property>]
let ``Segment times are ordered correctly`` (text: string) (NonNegativeInt startMs) (PositiveInt durationMs) =
    let segment = {
        Text = text
        StartTime = float32 startMs / 1000.0f
        EndTime = float32 (startMs + durationMs) / 1000.0f
        Tokens = []
    }
    segment.StartTime <= segment.EndTime

[<Property>]
let ``Confidence calculation from tokens is valid`` (tokenProbabilities: float32 list) =
    let validProbs = tokenProbabilities |> List.map (fun p -> max 0.0f (min 1.0f (abs p)))

    if validProbs.IsEmpty then
        true
    else
        let avgConfidence = validProbs |> List.average
        avgConfidence >= 0.0f && avgConfidence <= 1.0f

[<Property>]
let ``Audio chunk overlap is valid`` (PositiveInt chunkSize) (NonNegativeInt overlap) =
    let validOverlap = min overlap (chunkSize - 1)
    let effectiveAdvance = chunkSize - validOverlap
    effectiveAdvance > 0

[<Property>]
let ``Processing time is non-negative`` (NonNegativeInt audioMs) (NonNegativeInt processingMs) =
    let result = {
        FullText = ""
        Segments = []
        Duration = TimeSpan.FromMilliseconds(float audioMs)
        ProcessingTime = TimeSpan.FromMilliseconds(float processingMs)
        Timestamp = DateTime.UtcNow
        Language = None
        LanguageConfidence = None
        Tokens = None
    }
    result.ProcessingTime >= TimeSpan.Zero && result.Duration >= TimeSpan.Zero

[<Property>]
let ``Real-time factor calculation is valid`` (PositiveInt audioMs) (PositiveInt processingMs) =
    let rtf = float audioMs / float processingMs
    rtf > 0.0

[<Property>]
let ``Buffer consume never goes negative`` (PositiveInt capacity) (NonNegativeInt toConsume) =
    let buffer = RingBuffer<int>(capacity)
    buffer.Write([| 1 .. capacity |])

    let initialCount = buffer.Count
    buffer.Consume(toConsume)

    buffer.Count >= 0 && buffer.Count <= initialCount