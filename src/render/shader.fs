namespace Render

open SlimDX
open SlimDX.Direct3D11

// shader signature
type ShaderSignature(contents: byte array) =
    // signature object
    [<System.NonSerialized>]
    let mutable data = null

    // fixup callback
    member private this.Fixup device =
        let stream = new DataStream(contents, canRead = true, canWrite = false)
        data <- new SlimDX.D3DCompiler.ShaderSignature(stream)

    // resource accessor
    member this.Resource = data

// shader object
type ShaderObject<'T when 'T: null>(bytecode: byte array) =
    // shader object
    [<System.NonSerialized>]
    let mutable data = null

    // fixup callback
    member private this.Fixup device =
        use stream = new DataStream(bytecode, canRead = true, canWrite = false)
        use bcobj = new SlimDX.D3DCompiler.ShaderBytecode(stream)
        data <- System.Activator.CreateInstance(typedefof<'T>, [|device; box bcobj|]) :?> 'T

    // resource accessor
    member this.Resource = data

// shader
type Shader(vertex_signature: ShaderSignature, vertex: ShaderObject<VertexShader>, pixel: ShaderObject<PixelShader>) =
    // get input vertex signature
    member this.VertexSignature = vertex_signature

    // set shader to context
    member this.Set (context: DeviceContext) =
        context.VertexShader.Set(vertex.Resource)
        context.PixelShader.Set(pixel.Resource)