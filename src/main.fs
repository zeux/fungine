open System
open System.Drawing
open SlimDX
open SlimDX.DXGI
open SlimDX.Windows
open SlimDX.D3DCompiler
open SlimDX.Direct3D11

type ObjectPool(creator) =
    let s = System.Collections.Concurrent.ConcurrentStack()

    member x.get() =
        match s.TryPop() with
        | true, obj -> obj
        | _ -> creator ()

    member x.put(obj) =
        s.Push(obj)

let form = new RenderForm("fungine")
let desc = new SwapChainDescription(
            BufferCount = 1,
            ModeDescription = new ModeDescription(form.ClientSize.Width, form.ClientSize.Height, new Rational(60, 1), Format.R8G8B8A8_UNorm),
            IsWindowed = true,
            OutputHandle = form.Handle,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.Discard,
            Usage = Usage.RenderTargetOutput
            )

let (_, device, swapChain) = Device.CreateWithSwapChain(DriverType.Hardware, DeviceCreationFlags.None, desc)

let factory = swapChain.GetParent<Factory>()
factory.SetWindowAssociation(form.Handle, WindowAssociationFlags.IgnoreAll) |> ignore

let backBuffer = Texture2D.FromSwapChain<Texture2D>(swapChain, 0)
let renderView = new RenderTargetView(device, backBuffer)

let shader_path = "../src/shaders/passthrough_color.hlsl"
let bytecode_vs = ShaderBytecode.CompileFromFile(shader_path, "vs_main", "vs_5_0", ShaderFlags.None, EffectFlags.None)
let bytecode_ps = ShaderBytecode.CompileFromFile(shader_path, "ps_main", "ps_5_0", ShaderFlags.None, EffectFlags.None)

let vs = new VertexShader(device, bytecode_vs)
let ps = new PixelShader(device, bytecode_ps)

let layout = new InputLayout(device, bytecode_vs,
                [|
                InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0);
                InputElement("COLOR", 0, Format.R32G32B32A32_Float, 16, 0) 
                |])

let stream = new DataStream(int64 (3 * 32), true, true)

stream.WriteRange([|
                    Vector4(0.0f, 0.5f, 0.5f, 1.0f); Vector4(1.0f, 0.0f, 0.0f, 1.0f);
                    Vector4(0.5f, -0.5f, 0.5f, 1.0f); Vector4(0.0f, 1.0f, 0.0f, 1.0f);
                    Vector4(-0.5f, -0.5f, 0.5f, 1.0f); Vector4(0.0f, 0.0f, 1.0f, 1.0f)
                    |]
                    )

stream.Position <- int64 0

let vertices = new Buffer(device, stream, new BufferDescription(
                            BindFlags = BindFlags.VertexBuffer,
                            CpuAccessFlags = CpuAccessFlags.None,
                            OptionFlags = ResourceOptionFlags.None,
                            SizeInBytes = 3 * 32,
                            Usage = ResourceUsage.Default
                            ))

let contextHolder = new ObjectPool(fun _ -> new DeviceContext(device))

let draw (context: DeviceContext) x y width height =
    context.Rasterizer.SetViewports(new Viewport(float32 x, float32 y, float32 width, float32 height, 0.0f, 1.0f))

    context.OutputMerger.SetTargets(renderView)

    context.InputAssembler.InputLayout <- layout
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList
    context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vertices, 32, 0))

    context.VertexShader.Set(vs)
    context.PixelShader.Set(ps)
    context.Draw(3, 0)

    context.FinishCommandList(false)

let viewports width height sx sy = [ for x in 1..sx do for y in 1..sy -> (x - 1) * width / sx, (y - 1) * height / sy, width / sx, height / sy ]

MessagePump.Run(form, fun () ->

    device.ImmediateContext.ClearRenderTargetView(renderView, Color4 Color.Black)

    viewports form.ClientSize.Width form.ClientSize.Height 8 8
    |> Seq.map (fun (x, y, width, height) ->
        async {
        let c = contextHolder.get()
        let cl = draw c x y width height
        contextHolder.put(c)
        return cl
        } )
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Seq.iter (fun cl -> use c = cl in device.ImmediateContext.ExecuteCommandList(c, false))

    swapChain.Present(0, PresentFlags.None) |> ignore
)

device.ImmediateContext.ClearState()
device.ImmediateContext.Flush()
