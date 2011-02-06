namespace Build.Dae

open System

open Build.Dae.Parse

module TextureBuilder =
    // get relative path
    let private relativePath path root =
        Uri(root).MakeRelativeUri(Uri(path)).OriginalString

    // get texture path from id
    let build (doc: Document) id =
        // get image node
        let image = doc.Node id

        // get image path
        let path = image.SelectSingleNode("init_from/text()").Value
        let relative_path = relativePath path (Environment.CurrentDirectory + "/")

        // build texture object
        let source = relative_path
        let target = ".build/" + IO.Path.ChangeExtension(relative_path, ".dds")

        source, Render.Texture(target)