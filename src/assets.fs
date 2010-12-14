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
    let format = Render.VertexFormats.Pos_TBN_Tex1_Bone4_Packed

    // export meshes
    let instances = doc.Root.Select("/COLLADA/library_visual_scenes//node/instance_geometry | /COLLADA/library_visual_scenes//node/instance_controller")
    let meshes = instances |> Array.collect (fun i ->
        // build fat meshes from .dae
        let fat_meshes = Build.Dae.FatMeshBuilder.build doc i fvf skeleton

        // build packed & indexed meshes
        let packed_meshes = fat_meshes |> Array.map (fun mesh -> Build.Geometry.MeshPacker.pack mesh format)

        // optimize for Post T&L cache
        let postopt_meshes = packed_meshes |> Array.map (fun mesh -> { mesh with indices = Build.Geometry.PostTLOptimizerLinear.optimize mesh.indices })

        // optimize for Pre T&L cache
        let preopt_meshes = postopt_meshes |> Array.map (fun mesh ->
            let (vertices, indices) = Build.Geometry.PreTLOptimizer.optimize mesh.vertices mesh.indices mesh.format.size
            { mesh with vertices = vertices; indices = indices })

        preopt_meshes |> Array.map (fun mesh -> mesh, skeleton.data, skeleton.data.AbsoluteTransform skeleton.node_map.[i.ParentNode]))

    let time4 = timer.ElapsedMilliseconds

    printfn "parse / export tex / skeleton / mesh %d / %d / %d / %d" time1 (time2 - time1) (time3 - time2) (time4 - time3)

    meshes
    
let buildMeshes path =
    let patterns = [|"*.mb"; "*.ma"|]
    let files = patterns |> Array.collect (fun p -> System.IO.Directory.GetFiles(path, p, System.IO.SearchOption.AllDirectories))
    files |> Array.collect buildMesh

let buildAll () =
    buildMeshes "art"