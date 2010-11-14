module Build.Dae.Parse

open System.Xml
open System.Collections.Generic

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

let getFloatArray (file: File) id stride =
    let source = file.Node id

    let accessor = source.SelectSingleNode("technique_common/accessor")
    assert (accessor.Attributes.["stride"].Value = string stride)

    let array = file.Node accessor.Attributes.["source"].Value
    assert (array.Attributes.["count"].Value = accessor.Attributes.["count"].Value)

    let result = array.InnerText.Split(" \t\r\n".ToCharArray(), System.StringSplitOptions.RemoveEmptyEntries)
    assert (result.Length = int accessor.Attributes.["count"].Value)
    assert (result.Length % stride = 0)

    Array.map (fun n -> float n) result