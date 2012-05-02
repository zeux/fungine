module Build.Shader

open System.IO

open SlimDX
open SlimDX.D3DCompiler

open BuildSystem
open Render

// get byte array from data stream
let private getData (stream: DataStream) =
    stream.Position <- 0L
    stream.ReadRange<byte>(int stream.Length)

// include handler
type private IncludeHandler(includePaths, callback, rootFile) =
    interface Include with
        // open included file
        override this.Open(typ, file, parent, stream) =
            // get parent file name
            let parentFile =
                match parent with
                | null -> rootFile
                | :? FileStream as f -> f.Name
                | _ -> failwithf "Internal error: unknown stream %A" parent

            // look for file in the path list
            let paths =
                match typ with
                | IncludeType.Local -> Array.append [| Path.GetDirectoryName(parentFile) |] includePaths
                | IncludeType.System -> includePaths
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
let private buildBytecode path entry profile includePaths includeCallback =
    let flags = ShaderFlags.PackMatrixRowMajor ||| ShaderFlags.WarningsAreErrors
    ShaderBytecode.CompileFromFile(path, entry, profile, flags, EffectFlags.None, [||], IncludeHandler(includePaths, includeCallback, path))

// get shader parameters from bytecode
let private getParameters (code: ShaderBytecode) compute =
    use refl = new ShaderReflection(code)

    Array.init refl.Description.BoundResources (fun i ->
        let desc = refl.GetResourceBindingDescription(i)

        if desc.BindCount <> 1 then
            failwithf "Parameter %s occupies %d slots, array parameters are not supported" desc.Name desc.BindCount

        let binding =
            match desc.Type with
            | ShaderInputType.ConstantBuffer
                -> ShaderParameterBinding.ConstantBuffer
            | ShaderInputType.TextureBuffer | ShaderInputType.Texture
                -> ShaderParameterBinding.ShaderResource
            | ShaderInputType.Sampler
                -> ShaderParameterBinding.Sampler
            | ShaderInputType.Structured | ShaderInputType.RWStructured
            | ShaderInputType.ByteAddress | ShaderInputType.RWByteAddress
            | ShaderInputType.AppendStructured | ShaderInputType.ConsumeStructured
                -> if not compute then failwithf "Parameter %s is an unordered access view; such parameters are restricted to compute shaders" desc.Name
                   ShaderParameterBinding.UnorderedAccess
            | t when int t = 4 // SlimDX does not map D3D11_SIT_UAV_RWTYPED to an enum member
                -> if not compute then failwithf "Parameter %s is an unordered access view; such parameters are restricted to compute shaders" desc.Name
                   ShaderParameterBinding.UnorderedAccess
            | t -> failwithf "Parameter %s has unknown type %O" desc.Name t

        ShaderParameter(desc.Name, binding, desc.BindPoint))

// build shader
let private build source target version includePaths includeCallback =
    let compute = File.ReadAllLines(source).[0] = "//# compute"

    if compute then
        let cs = buildBytecode source "main" ("cs_" + version) includePaths includeCallback

        let shader = Program(ShaderObject(getData cs.Data, getParameters cs compute))

        Core.Serialization.Save.toFile target shader
    else
        let vs = buildBytecode source "vsMain" ("vs_" + version) includePaths includeCallback
        let vssig = ShaderSignature.GetInputSignature(vs)

        let ps = buildBytecode source "psMain" ("ps_" + version) includePaths includeCallback

        let shader =
            Shader(
                vertexSignature = ShaderSignature(getData vssig.Data),
                vertex = ShaderObject(getData vs.Data, getParameters vs compute),
                pixel = ShaderObject(getData ps.Data, getParameters ps compute))

        Core.Serialization.Save.toFile target shader

// shader builder object
type Builder(includePaths) =
    inherit BuildSystem.Builder("Shader", version = sprintf "I=%A" includePaths)

    // build shader
    override this.Build task =
        build task.Sources.[0].Path task.Targets.[0].Path "5_0" includePaths (fun path -> task.Implicit (Node path))
        None