module assets

open System.Collections.Generic

open Build.Geometry
open Build.Dae.Parse

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
    let timer = System.Diagnostics.Stopwatch.StartNew()

    let doc = Document(target)
    let time1 = timer.ElapsedMilliseconds

    // export textures
    let nodes = doc.Root.SelectNodes("/COLLADA/library_images/image/init_from/text()")
    for n in nodes do
        let path = System.Uri.UnescapeDataString(System.UriBuilder(n.Value).Path)
        let relative_path = relativePath path (System.Environment.CurrentDirectory + "/")
        buildTexture relative_path
    let time2 = timer.ElapsedMilliseconds

    // export skeleton
    let skeleton = Build.Dae.SkeletonBuilder.build doc
    let time3 = timer.ElapsedMilliseconds

    // use a constant FVF for now
    let fvf = [|Position; Tangent; Bitangent; Normal; TexCoord 0; SkinningInfo 4|]

    // export meshes
    let instances = doc.Root.Select("/COLLADA/library_visual_scenes//node/instance_geometry | /COLLADA/library_visual_scenes//node/instance_controller")
    let meshes = instances |> Array.collect (fun i -> (Build.Dae.FatMeshBuilder.build doc i fvf skeleton) |> Array.map (fun mesh -> mesh, skeleton.data.AbsoluteTransform skeleton.id_map.[i.ParentNode.Attribute "id"]))

    let time4 = timer.ElapsedMilliseconds

    printfn "parse / export tex / skeleton / mesh %d / %d / %d / %d" time1 (time2 - time1) (time3 - time2) (time4 - time3)

    meshes
    
let buildMeshes path =
    System.IO.Directory.GetFiles(path, "*.mb", System.IO.SearchOption.AllDirectories) |> Array.collect buildMesh

let buildAll () =
    buildMeshes "art"