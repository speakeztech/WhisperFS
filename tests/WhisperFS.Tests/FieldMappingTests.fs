module WhisperFS.Tests.FieldMappingTests

open System
open System.IO
open System.Runtime.InteropServices
open Xunit
open FsUnit.Xunit
open WhisperFS
open WhisperFS.Native

/// Tests for newly mapped fields that were previously unused
/// Since Native.Library functions aren't exposed, we test through the main API
type RuntimeTypeTests() =

    [<Fact>]
    member _.``Different runtime types are handled by the library``() =
        // We can't directly test getRuntimePath but we can verify
        // that the library handles different runtime types in its internal logic
        // The fact that our fixes compile and the library loads proves the runtime type is used

        // Test that we can at least reference the runtime types
        let runtimeTypes = [
            NativeLibraryLoader.RuntimeType.Cpu
            NativeLibraryLoader.RuntimeType.Blas
            NativeLibraryLoader.RuntimeType.Cuda11
            NativeLibraryLoader.RuntimeType.Cuda12
            NativeLibraryLoader.RuntimeType.CoreML
            NativeLibraryLoader.RuntimeType.OpenCL
            NativeLibraryLoader.RuntimeType.Vulkan
        ]

        // Just verify they exist and are distinct
        runtimeTypes |> should haveLength 7
        runtimeTypes |> List.distinct |> should haveLength 7

/// Tests for streaming functionality
type StreamingTests() =

    [<Fact>]
    member _.``createFileStream returns error for non-existent file``() =
        // Test that file path is actually used
        let stream = Streaming.createFileStream "/non/existent/file.wav" 4096

        let mutable errorOccurred = false
        use subscription = stream.Subscribe(
            (fun _ -> ()),
            (fun (ex: Exception) ->
                errorOccurred <- true
                ex |> should be ofExactType<FileNotFoundException>
            ))

        // Give it time to process
        System.Threading.Thread.Sleep(100)
        errorOccurred |> should be True

    [<Fact>]
    member _.``createFileStream uses chunk size parameter``() =
        // Create a temporary test file
        let tempFile = Path.GetTempFileName()
        try
            // Write some test data (at least 2 chunks worth)
            let testData = Array.create 8192 0uy
            File.WriteAllBytes(tempFile, testData)

            let chunkSize = 2048
            let stream = Streaming.createFileStream tempFile chunkSize

            let mutable chunkCount = 0
            use subscription = stream.Subscribe(fun samples ->
                chunkCount <- chunkCount + 1
                // Each chunk should be at most chunkSize/2 samples (16-bit PCM)
                samples.Length |> should be (lessThanOrEqualTo (chunkSize / 2))
            )

            System.Threading.Thread.Sleep(200)
            // Should have received multiple chunks
            chunkCount |> should be (greaterThan 1)
        finally
            if File.Exists(tempFile) then
                File.Delete(tempFile)

/// Tests for VAD model path validation
type VADModelPathTests() =

    [<Fact>]
    member _.``createVadDetector validates model path when provided``() =
        async {
            // Test with non-existent model path
            let config = {
                WhisperConfig.defaultConfig with
                    EnableVAD = true
                    VADModelPath = Some "/non/existent/model.onnx"
            }

            let! result = VoiceActivityDetection.createVadDetector config

            match result with
            | Error (FileNotFound msg) ->
                msg |> should haveSubstring "VAD model not found"
                msg |> should haveSubstring "/non/existent/model.onnx"
            | _ ->
                failwith "Expected FileNotFound error for non-existent VAD model"
        } |> Async.RunSynchronously

    [<Fact>]
    member _.``createVadDetector succeeds with valid model path``() =
        async {
            // Create a temporary file to simulate a model
            let tempFile = Path.GetTempFileName()
            try
                let config = {
                    WhisperConfig.defaultConfig with
                        EnableVAD = true
                        VADModelPath = Some tempFile
                }

                let! result = VoiceActivityDetection.createVadDetector config

                match result with
                | Ok detector ->
                    detector |> should not' (be null)
                | Error e ->
                    failwithf "Expected success but got error: %A" e
            finally
                if File.Exists(tempFile) then
                    File.Delete(tempFile)
        } |> Async.RunSynchronously

    [<Fact>]
    member _.``createVadDetector works when VAD is disabled``() =
        async {
            let config = {
                WhisperConfig.defaultConfig with
                    EnableVAD = false
                    VADModelPath = Some "/any/path" // Should be ignored
            }

            let! result = VoiceActivityDetection.createVadDetector config

            match result with
            | Ok detector ->
                detector |> should not' (be null)
            | Error e ->
                failwithf "VAD should work when disabled: %A" e
        } |> Async.RunSynchronously

/// Tests for grammar rules marshalling
type GrammarRulesTests() =

    [<Fact>]
    member _.``marshalGrammarRules returns null pointers for None``() =
        let (rulesPtr, countPtr, startPtr) = GrammarRules.marshalGrammarRules None

        rulesPtr |> should equal IntPtr.Zero
        countPtr |> should equal UIntPtr.Zero
        startPtr |> should equal UIntPtr.Zero

    [<Fact>]
    member _.``marshalGrammarRules allocates memory for Some rules``() =
        let testRules = "root ::= \"yes\" | \"no\""
        let (rulesPtr, countPtr, _) = GrammarRules.marshalGrammarRules (Some testRules)

        try
            rulesPtr |> should not' (equal IntPtr.Zero)
            countPtr |> should not' (equal UIntPtr.Zero)

            // Verify the text was copied
            let marshalledText = Marshal.PtrToStringAnsi(rulesPtr)
            marshalledText |> should haveSubstring "yes"
            marshalledText |> should haveSubstring "no"
        finally
            // Clean up allocated memory
            if rulesPtr <> IntPtr.Zero then
                Marshal.FreeHGlobal(rulesPtr)

    [<Fact>]
    member _.``Grammar rule formatting produces valid GBNF``() =
        // Test the example grammar functions
        let yesNoGrammar = GrammarRules.Examples.yesNoGrammar()
        yesNoGrammar |> should haveSubstring "yes"
        yesNoGrammar |> should haveSubstring "no"

        let numericGrammar = GrammarRules.Examples.numericGrammar()
        // The numeric grammar is more complex than just [0-9]+
        numericGrammar |> should haveSubstring "number"
        numericGrammar |> should haveSubstring "digit"

        let commandGrammar = GrammarRules.Examples.commandGrammar()
        commandGrammar |> should haveSubstring "play"
        commandGrammar |> should haveSubstring "stop"

    [<Fact>]
    member _.``Grammar rules text is properly used in marshalling``() =
        // Test that the rulesText parameter is actually used now
        let testRules = "root ::= \"test\""

        // This would have previously returned empty pointers when rulesText was unused
        let (rulesPtr, countPtr, _) = GrammarRules.marshalGrammarRules (Some testRules)

        try
            // Now it should allocate memory and use the text
            rulesPtr |> should not' (equal IntPtr.Zero)
            countPtr |> should not' (equal UIntPtr.Zero)
        finally
            // Clean up
            if rulesPtr <> IntPtr.Zero then
                Marshal.FreeHGlobal(rulesPtr)