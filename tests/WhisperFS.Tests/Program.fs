module WhisperFS.Tests.Program

open System

[<EntryPoint>]
let main argv =
    // This entry point is used when running tests from the command line
    // The actual test discovery and execution is handled by the test runner

    printfn "WhisperFS Test Suite"
    printfn "===================="
    printfn ""
    printfn "Run tests using:"
    printfn "  dotnet test                    # Run all tests"
    printfn "  dotnet test --filter Category=Unit    # Run unit tests only"
    printfn "  dotnet test --filter Category=Integration    # Run integration tests"
    printfn "  dotnet test --collect:\"XPlat Code Coverage\"    # With coverage"
    printfn ""
    printfn "For verbose output:"
    printfn "  dotnet test --logger \"console;verbosity=detailed\""
    printfn ""

    // Return 0 to indicate success
    // The actual test execution is handled by xUnit
    0