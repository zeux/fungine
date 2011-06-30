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

    // stringize for debugging
    override this.ToString() =
        match this with
        | String s -> sprintf "%A" s
        | Token c -> sprintf "%A" c
        | End -> "end of input"

// whether the character is a part of unquoted string value
let private isValueChar c =
    c = '-' || c = '+' || c = '.' || c = '_' || Char.IsLetterOrDigit(c)

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

// parse a comma-separated list up to the specified termination lexeme; newline is treated as comma if necessary
let private parseList (s: CharStream) term pred =
    let rec loop acc l =
        match l with
        | t when t = term -> acc
        | l ->
            let n = pred l
            let line = s.Location.Line

            match readLexeme s with
            | Token ',' -> loop (n :: acc) (readLexeme s)
            | t when t = term -> n :: acc
            | l ->
                // simulate comma if there was a newline instead
                if s.Location.Line > line then loop (n :: acc) l
                else fail s "Expected ',' or %A, got %A" term l

    // we accumulate results in a wrong order, so reverse before returning
    readLexeme s |> loop [] |> List.toArray |> Array.rev

// parse a list of fields into an Object, up to the specified termination lexeme
let rec private parseFields (s: CharStream) locs term =
    let start = s.Location

    parseList s term (function
        | String id ->
            let nextl =
                match readLexeme s with
                | Token '=' -> readLexeme s
                | l -> l

            id, parseNode s locs nextl
        | l -> fail s "Expected %A or value, got %A" term l)
    |> Object
    |> addNode locs start

// parse a node
and private parseNode s locs l =
    match l with
    | String c -> Value c |> addNode locs s.Location
    | Token '[' ->
        let start = s.Location

        parseList s (Token ']') (parseNode s locs)
        |> Array
        |> addNode locs start
    | Token '{' -> parseFields s locs (Token '}')
    | l -> fail s "Expected '[', '{' or value, got %A" l

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