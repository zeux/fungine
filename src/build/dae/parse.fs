module Build.Dae.Parse

open System.Xml
open System.Collections.Generic

// XmlNode helpers
type XmlNode with
    member this.Attribute (name: string) =
        this.Attributes.[name].Value

    member this.Select expr =
        this.SelectNodes(expr) |> Seq.cast<XmlNode> |> Seq.toArray

// COLLADA document with fast id -> node lookup
type Document(path: string) =
    let doc = XmlDocument()
    let ids = Dictionary<string, XmlNode>()
    do
        // load the document without namespaces so that XPath works
        use reader = new XmlTextReader(path, Namespaces = false)
        doc.Load(reader)

        // make id -> node mapping (ids should be unique)
        for n in doc.SelectNodes("//*[@id]") do
            ids.Add(n.Attribute "id", n)

    member this.Root = doc.DocumentElement
    member this.Node (id: string) = if id.[0] = '#' then ids.[id.Substring(1)] else ids.[id]

// parse whitespace-delimited string into array
let splitWhitespace (contents: string) =
    contents.Split(" \t\r\n".ToCharArray(), System.StringSplitOptions.RemoveEmptyEntries)

// precomputed table for fast integer exponentiation
let private fastPowerTenTable = [|1.f; 1e-1f; 1e-2f; 1e-3f; 1e-4f; 1e-5f; 1e-6f; 1e-7f; 1e-8f; 1e-9f; 1e-10f; 1e-11f; 1e-12f; 1e-13f; 1e-14f; 1e-15f; 1e-16f; 1e-17f; 1e-18f; 1e-19f; 1e-20f|]

// parse [s + offset, ...) as int without sign, adjust offset
let private fastIntParseUnsigned (s: string) (offset: int byref) =
    let mutable result = 0
    let mutable roffset = offset // register cache for performance
    let mutable digit = 0

    // cheat as much as we can, this is a hot path
    while roffset < s.Length && (digit <- int s.[roffset] - int '0'; uint32 digit <= 9u) do
        result <- result * 10 + digit
        roffset <- roffset + 1

    offset <- roffset
    result

// parse [s + offset, ...) as int with optional sign, adjust offset
let private fastIntParse (s: string) (offset: int byref) =
    if s.[offset] = '-' then
        offset <- offset + 1
        0 - fastIntParseUnsigned s &offset
    else
        fastIntParseUnsigned s &offset

// parse [s + offset, ...) as float without sign, adjust offset
let private fastFloatParseUnsigned (s: string) (offset: int byref) =
    // integer part
    let integer = float32 (fastIntParseUnsigned s &offset)

    // fractional part
    let mutable fractional = 0.f

    if offset < s.Length && s.[offset] = '.' then
        // skip dot
        let dotoffset = offset
        offset <- offset + 1

        // parse fractional part
        fractional <- float32 (fastIntParseUnsigned s &offset) * fastPowerTenTable.[offset - dotoffset - 1]

    // the final number except possible exponent
    let number = integer + fractional

    // exponent
    if offset < s.Length && (s.[offset] = 'e' || s.[offset] = 'E') then
        offset <- offset + 1
        let exponent = fastIntParse s &offset

        // use slow exponentiation - this should be a rare branch
        number * (10.f ** (float32 exponent))
    else
        number

// parse [s + offset, ...) as float with optional sign, adjust offset
let private fastFloatParse (s: string) (offset: int byref) =
    if s.[offset] = '-' then
        offset <- offset + 1
        0.f - fastFloatParseUnsigned s &offset
    else
        fastFloatParseUnsigned s &offset

// skip leading whitespaces, adjust offset
let private skipWhitespace (s: string) (offset: int byref) =
    // for performance reasons treat everything below space as whitespace
    while offset < s.Length && int s.[offset] <= 32 do
        offset <- offset + 1

// parse string contents as a float array
let parseFloatArray contents count =
    let result : float32 array = Array.zeroCreate count
    let mutable offset = 0

    // discard leading whitespace (fastFloatParse can't handle it)
    skipWhitespace contents &offset

    // parse the known number of numbers
    for i in 0 .. count - 1 do
        result.[i] <- fastFloatParse contents &offset
        skipWhitespace contents &offset

    // the string should be fully parsed, or the supplied count was incorrect
    assert (offset = contents.Length)

    result

// parse string contents as an int array
let parseIntArray contents =
    let result = List<int>()
    let mutable offset = 0

    // discard leading whitespace (fastIntParse can't handle it)
    skipWhitespace contents &offset

    // parse the entire string
    while offset < contents.Length do
        result.Add(fastIntParse contents &offset)
        skipWhitespace contents &offset

    result.ToArray()

// parse <source> with the given id as a float array with the desired stride
let getFloatArray (doc: Document) id stride =
    let source = doc.Node id

    // get accessor (we ignore the accessor attributes
    let accessor = source.SelectSingleNode("technique_common/accessor")
    assert (accessor.Attribute "stride" = string stride)

    // get the <float_array> node
    let array = doc.Node (accessor.Attribute "source")
    assert (int (array.Attribute "count") = stride * int (accessor.Attribute "count"))

    // parse whitespace-delimited string
    parseFloatArray array.InnerText (int (array.Attribute "count"))

// parse node contents as an integer array
let getIntArray (node: XmlNode) =
    parseIntArray node.InnerText

// parse <source> with the given id as a name array
let getNameArray (doc: Document) id =
    let source = doc.Node id

    // get accessor (we ignore the accessor attributes
    let accessor = source.SelectSingleNode("technique_common/accessor")
    assert (accessor.Attribute "stride" = "1")

    // get the <Name_array> node
    let array = doc.Node (accessor.Attribute "source")
    assert(array.Attribute "count" = accessor.Attribute "count")

    // parse whitespace-delimited string
    let result = splitWhitespace array.InnerText
    assert(result.Length = int (array.Attribute "count"))

    result