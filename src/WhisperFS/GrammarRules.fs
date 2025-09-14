namespace WhisperFS

open System
open System.Runtime.InteropServices
open WhisperFS.Native

/// Grammar rules for constraining transcription output
module GrammarRules =

    /// Grammar rule types for different constraint patterns
    type GrammarRule =
        | Keywords of words:string list          // Restrict to specific keywords
        | PhoneNumber                            // Phone number pattern
        | EmailAddress                            // Email pattern
        | URL                                     // URL pattern
        | Date                                    // Date pattern
        | Time                                    // Time pattern
        | Number                                  // Numeric pattern
        | Custom of pattern:string                // Custom BNF-like pattern

    /// Convert grammar rules to whisper.cpp format
    let private formatRule (rule: GrammarRule) =
        match rule with
        | Keywords words ->
            // Create a simple keyword-only grammar
            words
            |> List.map (fun w -> $"\"{w}\"")
            |> String.concat " | "
            |> sprintf "root ::= %s"

        | PhoneNumber ->
            """root ::= phone
phone ::= "(" area ")" " " prefix "-" line | area "-" prefix "-" line
area ::= digit digit digit
prefix ::= digit digit digit
line ::= digit digit digit digit
digit ::= "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" """

        | EmailAddress ->
            """root ::= email
email ::= username "@" domain
username ::= word ( "." word )*
domain ::= word ( "." word )* "." tld
word ::= letter ( letter | digit | "-" | "_" )*
tld ::= "com" | "org" | "net" | "edu" | "gov" | "io" | "co" | "uk"
letter ::= "a" | "b" | "c" | "d" | "e" | "f" | "g" | "h" | "i" | "j" | "k" | "l" | "m" | "n" | "o" | "p" | "q" | "r" | "s" | "t" | "u" | "v" | "w" | "x" | "y" | "z"
digit ::= "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" """

        | URL ->
            """root ::= url
url ::= protocol "://" domain path?
protocol ::= "http" | "https" | "ftp"
domain ::= subdomain ( "." subdomain )*
subdomain ::= word
path ::= "/" segment ( "/" segment )*
segment ::= word
word ::= letter ( letter | digit | "-" )*
letter ::= "a" | "b" | "c" | "d" | "e" | "f" | "g" | "h" | "i" | "j" | "k" | "l" | "m" | "n" | "o" | "p" | "q" | "r" | "s" | "t" | "u" | "v" | "w" | "x" | "y" | "z"
digit ::= "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" """

        | Date ->
            """root ::= date
date ::= month "/" day "/" year | year "-" month "-" day
month ::= "0" digit | "1" ( "0" | "1" | "2" )
day ::= ( "0" | "1" | "2" ) digit | "3" ( "0" | "1" )
year ::= "19" digit digit | "20" digit digit
digit ::= "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" """

        | Time ->
            """root ::= time
time ::= hour ":" minute ( ":" second )? ( " " period )?
hour ::= "0" digit | "1" ( "0" | "1" | "2" ) | digit
minute ::= ( "0" | "1" | "2" | "3" | "4" | "5" ) digit
second ::= ( "0" | "1" | "2" | "3" | "4" | "5" ) digit
period ::= "AM" | "PM" | "am" | "pm"
digit ::= "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" """

        | Number ->
            """root ::= number
number ::= integer | decimal
integer ::= digit+
decimal ::= integer "." digit+
digit ::= "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" """

        | Custom pattern -> pattern

    /// Grammar rules builder for complex patterns
    type GrammarBuilder() =
        let mutable rules = []

        member _.AddRule(rule: GrammarRule) =
            rules <- rule :: rules

        member _.AddKeywords(words: string list) =
            rules <- Keywords words :: rules

        member _.AddPattern(pattern: string) =
            rules <- Custom pattern :: rules

        member _.Build() =
            match rules with
            | [] -> None
            | [single] -> Some (formatRule single)
            | multiple ->
                // Combine multiple rules with alternation
                multiple
                |> List.rev
                |> List.map formatRule
                |> String.concat "\n"
                |> Some

    /// Helper to create grammar rules from common patterns
    let createGrammarFromPattern (pattern: string) =
        match pattern.ToLowerInvariant() with
        | "phone" | "phonenumber" -> Some (formatRule PhoneNumber)
        | "email" | "emailaddress" -> Some (formatRule EmailAddress)
        | "url" | "link" -> Some (formatRule URL)
        | "date" -> Some (formatRule Date)
        | "time" -> Some (formatRule Time)
        | "number" | "numeric" -> Some (formatRule Number)
        | _ -> None

    /// Parse and validate grammar rules
    let parseGrammarRules (rules: string option) =
        match rules with
        | None -> Ok None
        | Some rulesText ->
            // Basic validation - check for balanced parentheses and quotes
            let countChar c text =
                text |> Seq.filter ((=) c) |> Seq.length

            let openParens = countChar '(' rulesText
            let closeParens = countChar ')' rulesText
            let quotes = countChar '"' rulesText

            if openParens <> closeParens then
                Error (ConfigurationError "Grammar rules have unbalanced parentheses")
            elif quotes % 2 <> 0 then
                Error (ConfigurationError "Grammar rules have unbalanced quotes")
            else
                Ok (Some rulesText)

    /// Convert grammar rules to native format for whisper.cpp
    let marshalGrammarRules (rules: string option) =
        match rules with
        | None -> (IntPtr.Zero, UIntPtr.Zero, UIntPtr.Zero)
        | Some rulesText ->
            // Parse the rules text to create grammar elements
            // For now, we'll store the text and pass a pointer to it
            // In a full implementation, this would parse the GBNF format
            let rulesBytes = System.Text.Encoding.UTF8.GetBytes(rulesText + "\x00")
            let rulesPtr = Marshal.AllocHGlobal(rulesBytes.Length)
            Marshal.Copy(rulesBytes, 0, rulesPtr, rulesBytes.Length)

            // Return the pointer and counts
            // Note: The native side would need to parse the GBNF rules
            // For now we return the text pointer with count of 1 rule
            (rulesPtr, UIntPtr(uint32 1), UIntPtr.Zero)

    /// Example usage patterns
    module Examples =
        /// Restrict transcription to yes/no responses
        let yesNoGrammar() =
            formatRule (Keywords ["yes"; "no"; "maybe"; "I don't know"])

        /// Restrict to numeric input
        let numericGrammar() =
            formatRule Number

        /// Command grammar for voice assistant
        let commandGrammar() =
            let commands = [
                "play"; "pause"; "stop"; "next"; "previous";
                "volume up"; "volume down"; "mute"; "unmute";
                "open"; "close"; "save"; "load"; "exit"
            ]
            formatRule (Keywords commands)

        /// Medical terminology grammar
        let medicalGrammar() =
            let terms = [
                "blood pressure"; "heart rate"; "temperature";
                "systolic"; "diastolic"; "normal"; "elevated";
                "milligrams"; "milliliters"; "per day"
            ]
            formatRule (Keywords terms)