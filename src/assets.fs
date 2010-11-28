﻿module assets

let build source target func =
    let source_info = System.IO.FileInfo(source)
    let target_info = System.IO.FileInfo(target)

    if not target_info.Exists || source_info.LastWriteTime > target_info.LastWriteTime then
        System.IO.Directory.CreateDirectory(target_info.DirectoryName) |> ignore

        let result = func source target
        if not result then failwith "Error building asset"

let changeExtension name ext =
    System.IO.Path.ChangeExtension(name, ext)

let relativePath path root =
    let uri p = System.Uri(System.IO.Path.GetFullPath(p))
    let path_full = uri path
    let root_full = uri root
    root_full.MakeRelativeUri(path_full).OriginalString

let buildTexture source =
    build source (".build/" + changeExtension source ".dds") Build.Texture.build

let buildMesh source =
    // export .dae file
    let target = ".build/" + changeExtension source ".dae"
    build source target Build.Dae.Export.build

    // parse .dae file
    let doc = Build.Dae.Parse.Document(target)

    // export textures
    let nodes = doc.Root.SelectNodes("/COLLADA/library_images/image/init_from/text()")
    for n in nodes do
        let path = System.Uri.UnescapeDataString(System.UriBuilder(n.Value).Path)
        let relative_path = relativePath path (System.Environment.CurrentDirectory + "/")
        buildTexture relative_path

    // export meshes
    let instances = doc.Root.SelectNodes("/COLLADA/library_visual_scenes//node/instance_geometry | /COLLADA/library_visual_scenes//node/instance_controller");
    for n in instances do
        Build.Dae.FatMeshBuilder.build doc n |> Seq.length |> ignore
    
let buildMeshes path =
    System.IO.Directory.GetFiles(path, "*.mb", System.IO.SearchOption.AllDirectories) |> Seq.iter buildMesh

let buildAll () =
    buildMeshes "art"