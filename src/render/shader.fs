namespace Render

open System.Collections.Generic

open SlimDX
open SlimDX.Direct3D11

// shader parameter binding
type ShaderParameterBinding =
    | None = 0
    | ConstantBuffer = 1
    | ShaderResource = 2
    | Sampler = 3

// shader parameter registry
module ShaderParameterRegistry =
    let private slots = Dictionary<string, int>()

    // get existing unique slot for name or add new one
    let getSlot name =
       lock slots (fun () -> Core.CacheUtil.update slots name (fun _ -> slots.Count))

// shader parameter
[<Struct>]
type ShaderParameter(name: string, binding: ShaderParameterBinding, register: int) =
    // parameter slot id
    [<DefaultValue>] val mutable private slot: int

    // accessors
    member this.Slot = this.slot
    member this.Name = name
    member this.Binding = binding
    member this.Register = register

    // fixup callback
    member internal this.Fixup () =
        this.slot <- ShaderParameterRegistry.getSlot name

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
type ShaderObject<'T when 'T: null>(bytecode: byte array, parameters: ShaderParameter array) =
    // shader object
    [<System.NonSerialized>]
    let mutable data = null

    // fixup callback
    member private this.Fixup device =
        // create shader object
        use stream = new DataStream(bytecode, canRead = true, canWrite = false)
        use bcobj = new SlimDX.D3DCompiler.ShaderBytecode(stream)
        data <- System.Activator.CreateInstance(typeof<'T>, [|device; box bcobj|]) :?> 'T

        // fixup parameters
        parameters |> Array.iteri (fun i _ -> parameters.[i].Fixup())

    // resource accessor
    member this.Resource = data

    // parameter table accessor
    member this.Parameters = parameters

// shader
type Shader(vertexSignature: ShaderSignature, vertex: ShaderObject<VertexShader>, pixel: ShaderObject<PixelShader>) =
    // get vertex shader
    member this.VertexShader = vertex

    // get pixel shader
    member this.PixelShader = pixel

    // get input vertex signature
    member this.VertexSignature = vertexSignature

    // set shader to context
    member this.Set (context: DeviceContext) =
        context.VertexShader.Set(vertex.Resource)
        context.PixelShader.Set(pixel.Resource)
