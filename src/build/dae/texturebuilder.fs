namespace Build.Dae

open Build.Dae.Parse

module TextureBuilder =
    // get relative path
    let private relativePath path root =
        let uri p = System.Uri(System.IO.Path.GetFullPath(p))
        let path_full = uri path
        let root_full = uri root
        let result = root_full.MakeRelativeUri(path_full)
        result.OriginalString

    // build texture from controller instance
    let build (doc: Document) id =
        // get image node
        let image = doc.Node id

        // get image path
        let uri = image.SelectSingleNode("init_from/text()").Value
        let path = System.Uri.UnescapeDataString(System.UriBuilder(uri).Path)
        let relative_path = relativePath path (System.Environment.CurrentDirectory + "/")

        // return texture object
        Render.Texture(relative_path)