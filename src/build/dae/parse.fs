module Build.Dae.Parse

open System.Xml
open System.Collections.Generic

// COLLADA document with fast id -> node lookup
type File(path: string) =
    let doc = XmlDocument()
    let ids = Dictionary<string, XmlNode>()
    do
        // load the document without namespaces so that XPath works
        use reader = { new XmlTextReader(path) with override x.NamespaceURI = "" }
        doc.Load(reader)

        // make id -> node mapping (ids should be unique)
        for n in doc.SelectNodes("//*[@id]") do
            ids.Add(n.Attributes.["id"].Value, n)

    member x.Root = doc.DocumentElement
    member x.Node (id: string) = if id.[0] = '#' then ids.[id.Substring(1)] else ids.[id]

// parse whitespace-delimited string into array
let private splitWhitespace (contents: string) =
    contents.Split(" \t\r\n".ToCharArray(), System.StringSplitOptions.RemoveEmptyEntries)

// parse <source> with the given id as a float array with the desired stride
let getFloatArray (file: File) id stride =
    let source = file.Node id

    // get accessor (we ignore the accessor attributes
    let accessor = source.SelectSingleNode("technique_common/accessor")
    assert (accessor.Attributes.["stride"].Value = string stride)

    // get the <float_array> node
    let array = file.Node accessor.Attributes.["source"].Value
    assert (array.Attributes.["count"].Value = accessor.Attributes.["count"].Value)

    // parse whitespace-delimited string
    let result = splitWhitespace array.InnerText
    assert (result.Length = int accessor.Attributes.["count"].Value)
    assert (result.Length % stride = 0)

    // convert strings to floats
    Array.map (fun n -> float n) result

// parse node contents as an integer array
let getIntArray (node: XmlNode) =
    splitWhitespace node.InnerText |> Array.map (fun n -> int n)