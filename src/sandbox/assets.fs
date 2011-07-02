module assets

open Build
open BuildSystem

// build context
let context = Context(System.Environment.CurrentDirectory, ".build")

// watcher for asset build/reload
let watcher = lazy Core.FS.Watcher(".", fun path ->
    let node = Node path
    context.RunUpdated [| node |]
    AssetDB.tryReload node.Path)

// texture settings
Build.Texture.addSettings "art/texture.db"

// shader export
let Shader path =
    let bin = context.Target path ".shader"
    context.Task(Shader.builder, source = path, target = bin)

// mesh export
let Mesh path =
    // export .dae file
    let dae = context.Target path ".dae"
    context.Task(Dae.Export.builder, source = path, target = dae)

    // export .mesh file
    let mesh = context.Target path ".mesh"
    context.Task(Dae.MeshBuilder.builder, source = dae, target = mesh)

let buildMeshes path =
    let patterns = [|"*.mb"; "*.ma"; "*.max"|]
    let files = patterns |> Array.collect (fun p -> System.IO.Directory.GetFiles(path, p, System.IO.SearchOption.AllDirectories))
    files |> Array.iter (fun p -> Mesh (Node p))

let buildShaders path =
    let files = System.IO.Directory.GetFiles(path, "*.hlsl", System.IO.SearchOption.AllDirectories)
    files |> Array.iter (fun p -> Shader (Node p))

buildMeshes "art"
buildShaders "src/shaders"