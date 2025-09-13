module WhisperFS.Tests.Native.NativeMethodsTests

open System
open System.Runtime.InteropServices
open Xunit
open FsUnit.Xunit
open WhisperFS.Native

[<Fact>]
let ``Native method signatures are defined`` () =
    // This test verifies that the native method signatures compile
    // Actual invocation would require the native library

    // Check that delegate types exist
    typeof<WhisperNewSegmentCallback> |> should not' (be Null)
    typeof<WhisperEncoderBeginCallback> |> should not' (be Null)
    typeof<WhisperProgressCallback> |> should not' (be Null)
    typeof<WhisperLogitsFilterCallback> |> should not' (be Null)

[<Fact>]
let ``WhisperFullParams can be marshaled`` () =
    let mutable params = WhisperFullParams()
    params.strategy <- 0
    params.n_threads <- 4
    params.temperature <- 0.5f

    // Test marshaling
    let size = Marshal.SizeOf(params)
    let ptr = Marshal.AllocHGlobal(size)

    try
        Marshal.StructureToPtr(params, ptr, false)
        let unmarshaled = Marshal.PtrToStructure<WhisperFullParams>(ptr)

        unmarshaled.strategy |> should equal params.strategy
        unmarshaled.n_threads |> should equal params.n_threads
        unmarshaled.temperature |> should equal params.temperature
    finally
        Marshal.FreeHGlobal(ptr)

[<Fact>]
let ``Callback marshaling works correctly`` () =
    let mutable count = 0

    let callback = WhisperNewSegmentCallback(fun ctx state n_new userData ->
        count <- count + n_new
    )

    // Get function pointer
    let ptr = Marshal.GetFunctionPointerForDelegate(callback)
    ptr |> should not' (equal IntPtr.Zero)

    // Keep callback alive
    GC.KeepAlive(callback)

[<Fact>]
let ``Multiple callbacks can coexist`` () =
    let mutable segmentCount = 0
    let mutable encoderCalled = false
    let mutable progressValue = 0

    let segmentCallback = WhisperNewSegmentCallback(fun _ _ n _ ->
        segmentCount <- segmentCount + n
    )

    let encoderCallback = WhisperEncoderBeginCallback(fun _ _ _ ->
        encoderCalled <- true
        true
    )

    let progressCallback = WhisperProgressCallback(fun _ _ progress _ ->
        progressValue <- progress
    )

    // Simulate callbacks
    segmentCallback.Invoke(IntPtr.Zero, IntPtr.Zero, 5, IntPtr.Zero)
    encoderCallback.Invoke(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) |> ignore
    progressCallback.Invoke(IntPtr.Zero, IntPtr.Zero, 75, IntPtr.Zero)

    segmentCount |> should equal 5
    encoderCalled |> should be True
    progressValue |> should equal 75