module main

open System
open System.Drawing
open SlimDX
open SlimDX.DXGI
open SlimDX.Windows
open SlimDX.D3DCompiler
open SlimDX.Direct3D11

System.Environment.CurrentDirectory <- System.IO.Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "/.."
System.Console.WindowWidth <- max System.Console.WindowWidth 140

let meshes = assets.buildAll()

type ObjectPool(creator) =
    let s = System.Collections.Concurrent.ConcurrentStack()

    member x.get() =
        match s.TryPop() with
        | true, obj -> obj
        | _ -> creator ()

    member x.put(obj) =
        s.Push(obj)

type Effect(device, vscode, pscode) =
    let vs = new VertexShader(device, vscode)
    let ps = new PixelShader(device, pscode)
    let signature = ShaderSignature.GetInputSignature(vscode)

    new (device, path) =
        let bytecode_vs = ShaderBytecode.CompileFromFile(path, "vs_main", "vs_5_0", ShaderFlags.PackMatrixRowMajor, EffectFlags.None)
        let bytecode_ps = ShaderBytecode.CompileFromFile(path, "ps_main", "ps_5_0", ShaderFlags.PackMatrixRowMajor, EffectFlags.None)

        Effect(device, bytecode_vs, bytecode_ps)

    member x.VertexShader = vs
    member x.PixelShader = ps
    member x.VertexSignature = signature

let form = new RenderForm("fungine", Width = 1280, Height = 720)
let desc = new SwapChainDescription(
            BufferCount = 1,
            ModeDescription = new ModeDescription(form.ClientSize.Width, form.ClientSize.Height, new Rational(60, 1), Format.R8G8B8A8_UNorm),
            IsWindowed = true,
            OutputHandle = form.Handle,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.Discard,
            Usage = Usage.RenderTargetOutput
            )

let (_, device, swapChain) = Device.CreateWithSwapChain(DriverType.Hardware, DeviceCreationFlags.Debug, desc)

let factory = swapChain.GetParent<Factory>()
factory.SetWindowAssociation(form.Handle, WindowAssociationFlags.IgnoreAll) |> ignore

let backBuffer = Texture2D.FromSwapChain<Texture2D>(swapChain, 0)
let backBufferView = new RenderTargetView(device, backBuffer)

let depthBuffer = new Texture2D(device, Texture2DDescription(Width = form.ClientSize.Width, Height = form.ClientSize.Height, Format = Format.D24_UNorm_S8_UInt, ArraySize = 1, MipLevels = 1, SampleDescription = SampleDescription(1, 0), BindFlags = BindFlags.DepthStencil))
let depthBufferView = new DepthStencilView(device, depthBuffer)

let basic_textured = Effect(device, "src/shaders/basic_textured.hlsl")

let layout = new InputLayout(device, basic_textured.VertexSignature,
                [|
                InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0);
                InputElement("NORMAL", 0, Format.R32G32B32_Float, 12, 0);
                InputElement("TANGENT", 0, Format.R32G32B32_Float, 24, 0);
                InputElement("BITANGENT", 0, Format.R32G32B32_Float, 36, 0);
                InputElement("TEXCOORD", 0, Format.R32G32_Float, 48, 0) 
                |])

let createRenderVertexBuffer (vertices: Build.Geometry.FatVertex array) =
    let stream = new DataStream(56L * vertices.LongLength, true, true)

    for v in vertices do
        stream.Write(v.position)
        stream.Write(v.normal)
        stream.Write(v.tangent)
        stream.Write(v.bitangent)
        stream.Write(v.texcoord.[0])

    stream.Position <- 0L

    new Buffer(device, stream, BufferDescription(int stream.Length, ResourceUsage.Default, BindFlags.VertexBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 56))

let createRenderIndexBuffer (indices: int array) =
    let stream = new DataStream(4L * indices.LongLength, true, true)

    stream.WriteRange(indices)
    stream.Position <- 0L

    new Buffer(device, stream, BufferDescription(int stream.Length, ResourceUsage.Default, BindFlags.IndexBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 4))

let renderMeshes =
    meshes |> Array.map (fun (mesh, transform) ->
        mesh, transform, createRenderVertexBuffer mesh.vertices, createRenderIndexBuffer mesh.indices)

let projection = Matrix.PerspectiveFovLH(45.0f, float32 form.ClientSize.Width / float32 form.ClientSize.Height, 1.0f, 1000.0f)
let view = Matrix.LookAtLH(Vector3(0.0f, 40.0f, 20.0f), Vector3(0.0f, 25.0f, 0.0f), Vector3(0.0f, 1.0f, 0.0f))
let view_projection = view * projection

let constantBuffer = new Buffer(device, null, BufferDescription(128, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 128))

let albedo_map = Texture2D.FromFile(device, ".build/art/slave_driver/ch_barb_slavedriver_01.dds")
let normal_map = Texture2D.FromFile(device, ".build/art/slave_driver/ch_barb_slavedriver_01_nm.dds")
let specular_map = Texture2D.FromFile(device, ".build/art/slave_driver/ch_barb_slavedriver_01_spec.dds")

let contextHolder = new ObjectPool(fun _ -> new DeviceContext(device))

let draw (context: DeviceContext) =
    context.Rasterizer.SetViewports(new Viewport(0.0f, 0.0f, float32 form.ClientSize.Width, float32 form.ClientSize.Height))
    context.OutputMerger.SetTargets(depthBufferView, backBufferView)
    context.OutputMerger.DepthStencilState <- DepthStencilState.FromDescription(device, DepthStencilStateDescription(IsDepthEnabled = true, DepthWriteMask = DepthWriteMask.All, DepthComparison = Comparison.Less))

    context.InputAssembler.InputLayout <- layout
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList

    context.VertexShader.Set(basic_textured.VertexShader)
    context.PixelShader.Set(basic_textured.PixelShader)

    context.VertexShader.SetConstantBuffer(constantBuffer, 0)
    context.PixelShader.SetSampler(SamplerState.FromDescription(device, SamplerDescription(AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap, Filter = Filter.Anisotropic)), 0)

    context.PixelShader.SetShaderResource(new ShaderResourceView(device, albedo_map :> Resource), 0)
    context.PixelShader.SetShaderResource(new ShaderResourceView(device, normal_map :> Resource), 1)
    context.PixelShader.SetShaderResource(new ShaderResourceView(device, specular_map :> Resource), 2)

    for mesh, transform, vb, ib in renderMeshes do
        let box = context.MapSubresource(constantBuffer, 0, 128, MapMode.WriteDiscard, MapFlags.None)
        box.Data.Write(view_projection)
        box.Data.Write(transform)
        context.UnmapSubresource(constantBuffer, 0)

        context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vb, 56, 0))
        context.InputAssembler.SetIndexBuffer(ib, Format.R32_UInt, 0)
        context.DrawIndexed(mesh.indices.Length, 0, 0)

    context.FinishCommandList(false)

MessagePump.Run(form, fun () ->

    device.ImmediateContext.ClearRenderTargetView(backBufferView, Color4 Color.Black)
    device.ImmediateContext.ClearDepthStencilView(depthBufferView, DepthStencilClearFlags.Depth, 1.0f, 0uy)

    let context = contextHolder.get()
    use cl = draw context
    contextHolder.put(context)

    device.ImmediateContext.ExecuteCommandList(cl, false)

    swapChain.Present(0, PresentFlags.None) |> ignore
)

device.ImmediateContext.ClearState()
device.ImmediateContext.Flush()
