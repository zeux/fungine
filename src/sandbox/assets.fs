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
let shaderBuilder = Shader.Builder([|"src/shaders"; context.BuildPath + "/shaderstruct"|])

let Shader path =
    let bin = context.Target path ".shader"
    context.Task(shaderBuilder, source = path, target = bin)

// mesh export
let Mesh path =
    // export .dae file
    let dae = context.Target path ".dae"
    context.Task(Dae.Export.builder, source = path, target = dae)

    // export .mesh file
    let mesh = context.Target path ".mesh"
    context.Task(Dae.MeshBuilder.builder, source = dae, target = mesh)

// build shaders
Node.Glob "src/shaders/**.hlsl"
|> Array.iter Shader

// build meshes
[|"mb"; "ma"; "max"|]
|> Array.collect (fun ext -> Node.Glob (sprintf "art/**.%s" ext))
|> Array.iter Mesh

// build code for all shader struct types
System.AppDomain.CurrentDomain.GetAssemblies()
|> Array.collect (fun a -> a.GetTypes())
|> Array.filter (fun t -> t.IsDefined(typeof<Render.ShaderStructAttribute>, false))
|> Array.iter (fun t ->
    let path = context.Target (Node ("shaderstruct/auto_" + t.Name)) ".h"
    context.Task(ShaderStruct.builder, source = Node (t.FullName + ", " + t.Assembly.GetName().Name), target = path))
