module assets

let build source target func =
    let source_info = System.IO.FileInfo(source)
    let target_info = System.IO.FileInfo(target)

    if not target_info.Exists || source_info.LastWriteTime > target_info.LastWriteTime then
        System.IO.Directory.CreateDirectory(target_info.DirectoryName) |> ignore

        let result = func source target
        if not result then failwithf "Error building asset %s" target

let changeExtension name ext =
    System.IO.Path.ChangeExtension(name, ext)

let buildMesh source =
    // export .dae file
    let dae = ".build/" + changeExtension source ".dae"

    build source dae Build.Dae.Export.build

    // export .mesh file
    let target = ".build/" + changeExtension source ".mesh"

    build dae target (fun source target ->
        let textures = Build.Dae.MeshBuilder.build source target
        textures |> Seq.iter (fun (source, tex) -> build source tex.Path Build.Texture.build)
        true)

    // load .mesh file
    (Core.Serialization.Load.fromFile target) :?> Render.Mesh
    
let buildMeshes path =
    let patterns = [|"*.mb"; "*.ma"; "*.max"|]
    let files = patterns |> Array.collect (fun p -> System.IO.Directory.GetFiles(path, p, System.IO.SearchOption.AllDirectories))
    files |> Array.map buildMesh

let buildAll () =
    buildMeshes "art"
