module Build.Shader

open BuildSystem

open SlimDX
open SlimDX.D3DCompiler
open System.IO

// get byte array from data stream
let private getData (stream: DataStream) =
    stream.Position <- 0L
    stream.ReadRange<byte>(int stream.Length)

// include handler
type private IncludeHandler(include_paths, callback, root_file) =
    interface Include with
        // open included file
        override this.Open(typ, file, parent, stream) =
            // get parent file name
            let parent_file =
                match parent with
                | null -> root_file
                | :? FileStream as f -> f.Name
                | _ -> failwithf "Internal error: unknown stream %A" parent

            // look for file in the path list
            let paths =
                match typ with
                | IncludeType.Local -> Array.append [| Path.GetDirectoryName(parent_file) |] include_paths
                | IncludeType.System -> include_paths
                | _ -> failwithf "Internal error: unknown include type %A" typ

            // get a stream for the first file found; leave dependencies on all files so that we correctly rebuild if files get added
            stream <-
                paths |> Array.pick (fun p ->
                    let path = Path.Combine(p, file)
                    callback path
                    try
                        // additional exists check to avoid exceptions under normal circumstances
                        if File.Exists(path) then Some (File.OpenRead(path))
                        else None
                    with e -> None)

        // close included file
        override this.Close(stream) =
            stream.Close()

// build single bytecode instance
let private buildBytecode path entry profile include_paths include_callback =
    let flags = ShaderFlags.PackMatrixRowMajor ||| ShaderFlags.WarningsAreErrors
    ShaderBytecode.CompileFromFile(path, entry, profile, flags, EffectFlags.None, [||], IncludeHandler(include_paths, include_callback, path))

// get shader parameters from bytecode
let private getParameters (code: ShaderBytecode) =
    use refl = new ShaderReflection(code)

    Array.init refl.Description.BoundResources (fun i ->
        let desc = refl.GetResourceBindingDescription(i)

        if desc.BindCount <> 1 then failwithf "Parameter %s occupies %d slots, array parameters are not supported" desc.Name desc.BindCount

        let binding =
            match desc.Type with
            | ShaderInputType.ConstantBuffer -> Render.ShaderParameterBinding.ConstantBuffer
            | ShaderInputType.Sampler -> Render.ShaderParameterBinding.Sampler
            | _ -> Render.ShaderParameterBinding.ShaderResource

        Render.ShaderParameter(desc.Name, binding, desc.BindPoint))

// build shader
let private build source target version include_paths include_callback =
    let vs = buildBytecode source "vs_main" ("vs_" + version) include_paths include_callback
    let vssig = ShaderSignature.GetInputSignature(vs)

    let ps = buildBytecode source "ps_main" ("ps_" + version) include_paths include_callback

    let shader =
        Render.Shader(
            vertex_signature = Render.ShaderSignature(getData vssig.Data),
            vertex = Render.ShaderObject(getData vs.Data, getParameters vs),
            pixel = Render.ShaderObject(getData ps.Data, getParameters ps))

    Core.Serialization.Save.toFile target shader

// shader builder object
type Builder(include_paths) =
    inherit BuildSystem.Builder("Shader", version = sprintf "I=%A" include_paths)

    // build shader
    override this.Build task =
        build task.Sources.[0].Path task.Targets.[0].Path "5_0" include_paths (fun path -> task.Implicit (Node path))
        None