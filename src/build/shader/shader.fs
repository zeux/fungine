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
type private IncludeHandler(callback, root_file) =
    interface Include with
        // open included file
        override this.Open(typ, file, parent, stream) =
            // get parent file name
            let parent_file =
                match parent with
                | null -> root_file
                | :? FileStream as f -> f.Name
                | _ -> failwithf "Internal error: unknown stream %A" parent

            // make full file name
            let path = Path.Combine(Path.GetDirectoryName(parent_file), file)

            // open file
            callback path
            stream <- try File.OpenRead(path) with e -> null

        // close included file
        override this.Close(stream) =
            stream.Dispose()

// build single bytecode instance
let private buildBytecode path entry profile includes =
    let flags = ShaderFlags.PackMatrixRowMajor ||| ShaderFlags.WarningsAreErrors
    ShaderBytecode.CompileFromFile(path, entry, profile, flags, EffectFlags.None, [||], IncludeHandler(includes, path))

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
let private build source target version includes =
    let vs = buildBytecode source "vs_main" ("vs_" + version) includes
    let vssig = ShaderSignature.GetInputSignature(vs)

    let ps = buildBytecode source "ps_main" ("ps_" + version) includes

    let shader =
        Render.Shader(
            vertex_signature = Render.ShaderSignature(getData vssig.Data),
            vertex = Render.ShaderObject(getData vs.Data, getParameters vs),
            pixel = Render.ShaderObject(getData ps.Data, getParameters ps))

    Core.Serialization.Save.toFile target shader

// shader builder object
let builder = ActionBuilder("Shader", fun task ->
    build task.Sources.[0].Path task.Targets.[0].Path "5_0" (fun path -> task.Implicit (Node path)))