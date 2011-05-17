module Build.Dae.TextureBuilder

open System

open Build.Dae.Parse
open BuildSystem

// get texture path from id
let build (doc: Document) id =
    // get image node
    let image = doc.Node id

    // get image path
    let path = image.SelectSingleNode("init_from/text()").Value

    // build texture object
    let source = Node (Uri(path).AbsolutePath)
    let target = Context.Current.Target source ".dds"

    source.Path, Render.Texture(target.Path)