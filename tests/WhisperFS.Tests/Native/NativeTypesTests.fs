module WhisperFS.Tests.Native.NativeTypesTests

open System
open System.Runtime.InteropServices
open Xunit
open FsUnit.Xunit
open WhisperFS.Native

[<Fact>]
let ``WhisperFullParams struct has correct size`` () =
    // Ensure the struct size matches expected layout
    let size = Marshal.SizeOf<WhisperFullParams>()
    size |> should be (greaterThan 0)

[<Fact>]
let ``WhisperFullParams default values are sensible`` () =
    let mutable params = WhisperFullParams()

    // Set default values
    params.strategy <- 0  // GREEDY
    params.n_threads <- Environment.ProcessorCount
    params.translate <- false
    params.no_context <- false
    params.single_segment <- false
    params.token_timestamps <- false
    params.temperature <- 0.0f
    params.suppress_blank <- true
    params.suppress_non_speech_tokens <- true

    params.strategy |> should equal 0
    params.n_threads |> should be (greaterThan 0)
    params.translate |> should be False
    params.suppress_blank |> should be True

[<Fact>]
let ``WhisperFullParams can be configured for streaming`` () =
    let mutable params = WhisperFullParams()

    // Configure for streaming
    params.single_segment <- false
    params.no_context <- false
    params.token_timestamps <- true
    params.max_tokens <- 0  // No limit

    params.single_segment |> should be False
    params.no_context |> should be False
    params.token_timestamps |> should be True
    params.max_tokens |> should equal 0

[<Fact>]
let ``WhisperFullParams temperature settings`` () =
    let mutable params = WhisperFullParams()

    params.temperature <- 0.0f  // Deterministic
    params.temperature_inc <- 0.2f
    params.entropy_thold <- 2.4f
    params.logprob_thold <- -1.0f

    params.temperature |> should equal 0.0f
    params.temperature_inc |> should equal 0.2f

[<Fact>]
let ``WhisperFullParams beam search configuration`` () =
    let mutable params = WhisperFullParams()

    params.strategy <- 1  // BEAM_SEARCH
    params.beam_size <- 5
    params.best_of <- 5
    params.patience <- 0.0f

    params.strategy |> should equal 1
    params.beam_size |> should equal 5
    params.best_of |> should equal 5

[<Fact>]
let ``Callback delegates can be created`` () =
    let mutable callbackInvoked = false

    let segmentCallback = WhisperNewSegmentCallback(fun ctx state n_new userData ->
        callbackInvoked <- true
    )

    // Simulate callback invocation
    segmentCallback.Invoke(IntPtr.Zero, IntPtr.Zero, 1, IntPtr.Zero)

    callbackInvoked |> should be True

[<Fact>]
let ``EncoderBeginCallback returns boolean`` () =
    let mutable shouldProceed = true

    let encoderCallback = WhisperEncoderBeginCallback(fun ctx state userData ->
        shouldProceed
    )

    let result = encoderCallback.Invoke(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)
    result |> should equal shouldProceed

    shouldProceed <- false
    let result2 = encoderCallback.Invoke(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)
    result2 |> should be False

[<Fact>]
let ``ProgressCallback receives progress updates`` () =
    let mutable lastProgress = -1

    let progressCallback = WhisperProgressCallback(fun ctx state progress userData ->
        lastProgress <- progress
    )

    progressCallback.Invoke(IntPtr.Zero, IntPtr.Zero, 50, IntPtr.Zero)
    lastProgress |> should equal 50

    progressCallback.Invoke(IntPtr.Zero, IntPtr.Zero, 100, IntPtr.Zero)
    lastProgress |> should equal 100

[<Fact>]
let ``WhisperFullParams timing configuration`` () =
    let mutable params = WhisperFullParams()

    params.offset_ms <- 1000  // Start at 1 second
    params.duration_ms <- 5000  // Process 5 seconds
    params.max_len <- 100  // Max 100 characters per segment

    params.offset_ms |> should equal 1000
    params.duration_ms |> should equal 5000
    params.max_len |> should equal 100

[<Fact>]
let ``WhisperFullParams timestamp threshold settings`` () =
    let mutable params = WhisperFullParams()

    params.thold_pt <- 0.01f
    params.thold_ptsum <- 0.01f
    params.no_timestamps <- false
    params.print_timestamps <- true

    params.thold_pt |> should equal 0.01f
    params.thold_ptsum |> should equal 0.01f
    params.no_timestamps |> should be False
    params.print_timestamps |> should be True

[<Fact>]
let ``WhisperFullParams language detection`` () =
    let mutable params = WhisperFullParams()

    params.detect_language <- true
    params.language <- IntPtr.Zero  // Auto-detect

    params.detect_language |> should be True
    params.language |> should equal IntPtr.Zero

[<Fact>]
let ``WhisperFullParams suppression settings`` () =
    let mutable params = WhisperFullParams()

    params.suppress_blank <- true
    params.suppress_non_speech_tokens <- true
    params.max_initial_ts <- 1.0f
    params.length_penalty <- -1.0f

    params.suppress_blank |> should be True
    params.suppress_non_speech_tokens |> should be True
    params.max_initial_ts |> should equal 1.0f
    params.length_penalty |> should equal -1.0f

[<Fact>]
let ``WhisperFullParams callback pointers`` () =
    let mutable params = WhisperFullParams()

    // Initially null
    params.new_segment_callback |> should equal IntPtr.Zero
    params.encoder_begin_callback |> should equal IntPtr.Zero
    params.logits_filter_callback |> should equal IntPtr.Zero

    // Can be set to function pointers
    let callback = WhisperNewSegmentCallback(fun _ _ _ _ -> ())
    let handle = GCHandle.Alloc(callback)
    let ptr = Marshal.GetFunctionPointerForDelegate(callback)

    params.new_segment_callback <- ptr
    params.new_segment_callback |> should not' (equal IntPtr.Zero)

    handle.Free()

[<Fact>]
let ``WhisperFullParams prompt tokens configuration`` () =
    let mutable params = WhisperFullParams()

    params.prompt_tokens <- IntPtr.Zero
    params.prompt_n_tokens <- 0

    params.prompt_tokens |> should equal IntPtr.Zero
    params.prompt_n_tokens |> should equal 0

    // Simulate setting prompt tokens
    let tokens = [| 1; 2; 3; 4 |]
    let handle = GCHandle.Alloc(tokens, GCHandleType.Pinned)
    params.prompt_tokens <- handle.AddrOfPinnedObject()
    params.prompt_n_tokens <- tokens.Length

    params.prompt_n_tokens |> should equal 4
    handle.Free()