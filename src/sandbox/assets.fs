module assets

open Build
open BuildSystem

open System.IO

let context = Context(System.Environment.CurrentDirectory, ".build")

// create target name
let getTarget (source: Node) ext = Node (Path.Combine(context.BuildPath, Path.ChangeExtension(source.Path, ext)))

// mesh export
let Mesh path =
    // export .dae file
    let dae = getTarget path ".dae"
    context.Task(Dae.Export.builder, source = path, target = dae)

    // export .mesh file
    let mesh = getTarget path ".mesh"
    context.Task(Dae.MeshBuilder.builder, source = dae, target = mesh)

let buildMeshes path =
    let patterns = [|"*.mb"; "*.ma"; "*.max"|]
    let files = patterns |> Array.collect (fun p -> System.IO.Directory.GetFiles(path, p, System.IO.SearchOption.AllDirectories))
    files |> Array.iter (fun p -> Mesh (Node p))

buildMeshes "art"