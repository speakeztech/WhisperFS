module WhisperFS.Tests.GrammarTests

open System
open Xunit
open WhisperFS
open WhisperFS.GrammarRules

[<Fact>]
let ``Keywords grammar rule generates correct format`` () =
    // Arrange
    let rule = Keywords ["yes"; "no"; "maybe"]

    // Act
    let builder = GrammarBuilder()
    builder.AddRule(rule)
    let result = builder.Build()

    // Assert
    match result with
    | Some grammar ->
        Assert.Contains("\"yes\"", grammar)
        Assert.Contains("\"no\"", grammar)
        Assert.Contains("\"maybe\"", grammar)
        Assert.Contains("|", grammar)
        Assert.StartsWith("root ::=", grammar)
    | None ->
        Assert.True(false, "Grammar should be generated")

[<Fact>]
let ``PhoneNumber grammar rule generates phone pattern`` () =
    // Arrange
    let rule = PhoneNumber

    // Act
    let builder = GrammarBuilder()
    builder.AddRule(rule)
    let result = builder.Build()

    // Assert
    match result with
    | Some grammar ->
        Assert.Contains("phone", grammar)
        Assert.Contains("area", grammar)
        Assert.Contains("prefix", grammar)
        Assert.Contains("digit", grammar)
        Assert.Contains("\"0\" | \"1\" | \"2\"", grammar)
    | None ->
        Assert.True(false, "Grammar should be generated")

[<Fact>]
let ``EmailAddress grammar rule generates email pattern`` () =
    // Arrange
    let rule = EmailAddress

    // Act
    let builder = GrammarBuilder()
    builder.AddRule(rule)
    let result = builder.Build()

    // Assert
    match result with
    | Some grammar ->
        Assert.Contains("email", grammar)
        Assert.Contains("username", grammar)
        Assert.Contains("@", grammar)
        Assert.Contains("domain", grammar)
        Assert.True(grammar.Contains("com"), $"Grammar should contain 'com'. Length: {grammar.Length}")
    | None ->
        Assert.True(false, "Grammar should be generated")

[<Fact>]
let ``Custom grammar rule uses provided pattern`` () =
    // Arrange
    let customPattern = "root ::= \"hello\" | \"world\""
    let rule = Custom customPattern

    // Act
    let builder = GrammarBuilder()
    builder.AddRule(rule)
    let result = builder.Build()

    // Assert
    match result with
    | Some grammar ->
        Assert.Equal(customPattern, grammar)
    | None ->
        Assert.True(false, "Grammar should be generated")

[<Fact>]
let ``createGrammarFromPattern recognizes common patterns`` () =
    // Test phone pattern
    let phoneGrammar = createGrammarFromPattern "phone"
    Assert.True(phoneGrammar.IsSome)
    Assert.Contains("phone", phoneGrammar.Value)

    // Test email pattern
    let emailGrammar = createGrammarFromPattern "email"
    Assert.True(emailGrammar.IsSome)
    Assert.Contains("email", emailGrammar.Value)

    // Test URL pattern
    let urlGrammar = createGrammarFromPattern "url"
    Assert.True(urlGrammar.IsSome)
    Assert.Contains("url", urlGrammar.Value)

    // Test unknown pattern
    let unknownGrammar = createGrammarFromPattern "unknown"
    Assert.True(unknownGrammar.IsNone)

[<Fact>]
let ``parseGrammarRules validates balanced parentheses`` () =
    // Valid grammar with balanced parentheses
    let validGrammar = Some "root ::= (\"yes\" | \"no\")"
    let validResult = parseGrammarRules validGrammar

    match validResult with
    | Ok (Some _) -> Assert.True(true)
    | _ -> Assert.True(false, "Valid grammar should parse")

    // Invalid grammar with unbalanced parentheses
    let invalidGrammar = Some "root ::= (\"yes\" | \"no\""
    let invalidResult = parseGrammarRules invalidGrammar

    match invalidResult with
    | Error (ConfigurationError msg) ->
        Assert.Contains("unbalanced parentheses", msg)
    | _ -> Assert.True(false, "Should detect unbalanced parentheses")

[<Fact>]
let ``parseGrammarRules validates balanced quotes`` () =
    // Valid grammar with balanced quotes
    let validGrammar = Some "root ::= \"yes\" | \"no\""
    let validResult = parseGrammarRules validGrammar

    match validResult with
    | Ok (Some _) -> Assert.True(true)
    | _ -> Assert.True(false, "Valid grammar should parse")

    // Invalid grammar with unbalanced quotes
    let invalidGrammar = Some "root ::= \"yes | \"no\""
    let invalidResult = parseGrammarRules invalidGrammar

    match invalidResult with
    | Error (ConfigurationError msg) ->
        Assert.Contains("unbalanced quotes", msg)
    | _ -> Assert.True(false, "Should detect unbalanced quotes")

[<Fact>]
let ``GrammarBuilder combines multiple rules`` () =
    // Arrange
    let builder = GrammarBuilder()
    builder.AddKeywords(["start"; "stop"])
    builder.AddKeywords(["pause"; "resume"])

    // Act
    let result = builder.Build()

    // Assert
    match result with
    | Some grammar ->
        // Should contain both sets of keywords
        Assert.Contains("\"start\"", grammar)
        Assert.Contains("\"stop\"", grammar)
        Assert.Contains("\"pause\"", grammar)
        Assert.Contains("\"resume\"", grammar)
    | None ->
        Assert.True(false, "Grammar should be generated")

[<Fact>]
let ``Example grammars generate expected patterns`` () =
    // Test yes/no grammar
    let yesNo = Examples.yesNoGrammar()
    Assert.Contains("\"yes\"", yesNo)
    Assert.Contains("\"no\"", yesNo)

    // Test numeric grammar
    let numeric = Examples.numericGrammar()
    Assert.Contains("number", numeric)
    Assert.Contains("digit", numeric)

    // Test command grammar
    let commands = Examples.commandGrammar()
    Assert.Contains("\"play\"", commands)
    Assert.Contains("\"pause\"", commands)
    Assert.Contains("\"stop\"", commands)

    // Test medical grammar
    let medical = Examples.medicalGrammar()
    Assert.Contains("\"blood pressure\"", medical)
    Assert.Contains("\"heart rate\"", medical)

[<Fact>]
let ``WhisperConfig with grammar rules is properly configured`` () =
    // Arrange
    let grammarRules = Some (Examples.yesNoGrammar())
    let config = { WhisperConfig.defaultConfig with
                    ModelType = Tiny
                    GrammarRules = grammarRules }

    // Act & Assert
    Assert.True(config.GrammarRules.IsSome)
    Assert.Contains("yes", config.GrammarRules.Value)