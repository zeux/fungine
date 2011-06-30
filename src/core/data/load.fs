module Core.Data.Load

open System
open System.Collections.Generic
open System.IO
open System.Text

open Core.Data

// CharStream EOF sign, useable in pattern matching constructs
let [<Literal>] EOF = '\000'

// stream of characters with line counting
type CharStream(path: string, s: TextReader) =
    let toChar = function | -1 -> EOF | c -> char c
    let mutable line = 1

    // look at the current character, don't move read position
    member this.Peek () = s.Peek() |> toChar

    // read the current character, move read position (unless it's at EOF)
    member this.Read () =
        let c = s.Read() |> toChar
        if c = '\n' then line <- line + 1
        c

    // get current location (path + line)
    member this.Location = Location(path, line)

// stop parsing with exception, report current location in stream
let private fail (s: CharStream) fmt =
    Printf.ksprintf (fun err -> failwithf "%A: %s" s.Location err) fmt

// lexeme type
type private Lexeme =
| String of string
| Token of char
| End

// whether the character is a part of unquoted string value
let private isValueChar c =
    c = '-' || c = '.' || c = '_' || Char.IsLetterOrDigit(c)

// read the escape sequence inside quoted string (after \)
let private readEscapeSequence (s: CharStream) =
    match s.Read() with
    | EOF -> fail s "Unexpected end of input (unterminated escape sequence)"
    | 't' -> '\t'
    | 'r' -> '\r'
    | 'n' -> '\n'
    | c -> c

// read quoted string (delimited by double quotes)
let private readQuotedString (s: CharStream) =
    let sb = StringBuilder()
    let rec loop () =
        match s.Read() with
        | EOF -> fail s "Unexpected end of input (unterminated string)"
        | '"' -> ()
        | '\\' ->
            sb.Append(readEscapeSequence s) |> ignore
            loop ()
        | c ->
            sb.Append(c) |> ignore
            loop ()
    loop ()
    sb.ToString()

// read the tail of unquoted string (except first character)
let private readString (first: char) (s: CharStream) =
    let sb = StringBuilder()
    sb.Append(first) |> ignore
    while isValueChar (s.Peek()) do
        sb.Append(s.Read()) |> ignore
    sb.ToString()

// read next lexeme
let rec private readLexeme (s: CharStream) =
    match s.Read() with
    | EOF -> End
    | '\t' | '\r' | '\n' | ' ' -> readLexeme s
    | '/' ->
        // skip comments
        match s.Read() with
        | '/' ->
            while s.Peek() <> EOF && s.Peek() <> '\n' do s.Read() |> ignore
            readLexeme s
        | c -> fail s "Unexpected character '%c' (expected '/')" c
    | '=' | '{' | '}' | '[' | ']' | ',' as c -> Token c
    | '"' -> String (readQuotedString s)
    | c when isValueChar c -> String (readString c s)
    | c -> fail s "Unknown character '%c'" c

// location map filling helper
let private addNode (locations: IDictionary<Node, Location>) loc node =
    locations.Add(node, loc)
    node

// parse a list of fields into an Object, up to the specified termination lexeme
let rec private parseFields s locs term =
    let rec loop acc =
        match readLexeme s with
        | String id ->
            let value =
                match readLexeme s with
                | Token '=' ->
                    parseNode s locs (readLexeme s)
                | l -> parseNode s locs l
            loop ((id, value) :: acc)
        | t when t = term -> acc
        | l -> fail s "Expected '%A' or identifier, got '%A'" term s
    Object (loop [] |> List.toArray |> Array.rev) |> addNode locs s.Location

// parse a node
and private parseNode s locs l =
    match l with
    | String c -> Value c |> addNode locs s.Location
    | Token '[' ->
        let rec loop acc =
            match readLexeme s with
            | Token ']' -> acc
            | l ->
                let n = parseNode s locs l
                match readLexeme s with
                | Token ',' -> loop (n :: acc)
                | Token ']' -> n :: acc
                | l -> fail s "Expected ',' or ']', got '%A'" l
        Array (loop [] |> List.toArray |> Array.rev) |> addNode locs s.Location
    | Token '{' ->
        parseFields s locs (Token '}')
    | l -> fail s "Unexpected token '%A'" l

// load document from stream
let fromStream (stream: Stream) path =
    use reader = new StreamReader(stream)
    let locs = Dictionary<_, _>(HashIdentity.Reference)
    let root = parseFields (CharStream(path, reader)) locs End
    Document(root, locs)

// load document from file
let fromFile path =
    use s = File.OpenRead(path)
    fromStream s path