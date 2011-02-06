module main

open System
open System.Collections.Generic
open System.Drawing
open SlimDX
open SlimDX.DXGI
open SlimDX.Windows
open SlimDX.D3DCompiler
open SlimDX.Direct3D11

[<STAThread>]
do()

let dbg_stress_test = Core.DbgVar(false, "render/stress test")
let dbg_wireframe = Core.DbgVar(false, "render/wireframe")
let dbg_present_interval = Core.DbgVar(0, "vsync interval")
let dbg_name = Core.DbgVar("foo", "name")
let dbg_fillmode = Core.DbgVar(FillMode.Solid, "render/fill mode")
let dbg_texfilter = Core.DbgVar(Filter.Anisotropic, "render/texture/filter")

System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo.InvariantCulture
System.Environment.CurrentDirectory <- System.IO.Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "/../.."
System.Console.WindowWidth <- max System.Console.WindowWidth 140

let meshes = assets.buildAll()

type ObjectPool(creator) =
    let s = System.Collections.Concurrent.ConcurrentStack()

    member this.get() =
        match s.TryPop() with
        | true, obj -> obj
        | _ -> creator ()

    member this.put(obj) =
        s.Push(obj)

type Effect(device, vscode, pscode) =
    let vs = new VertexShader(device, vscode)
    let ps = new PixelShader(device, pscode)
    let signature = ShaderSignature.GetInputSignature(vscode)

    new (device, path) =
        let flags = ShaderFlags.PackMatrixRowMajor ||| ShaderFlags.WarningsAreErrors
        let bytecode_vs = ShaderBytecode.CompileFromFile(path, "vs_main", "vs_5_0", flags, EffectFlags.None)
        let bytecode_ps = ShaderBytecode.CompileFromFile(path, "ps_main", "ps_5_0", flags, EffectFlags.None)

        Effect(device, bytecode_vs, bytecode_ps)

    member this.VertexShader = vs
    member this.PixelShader = ps
    member this.VertexSignature = signature

let form = new RenderForm("fungine", Width = 1280, Height = 720)
let desc = new SwapChainDescription(
            BufferCount = 1,
            ModeDescription = ModeDescription(form.ClientSize.Width, form.ClientSize.Height, Rational(0, 0), Format.R8G8B8A8_UNorm),
            IsWindowed = true,
            OutputHandle = form.Handle,
            SampleDescription = SampleDescription(1, 0),
            SwapEffect = SwapEffect.Discard,
            Usage = Usage.RenderTargetOutput,
            Flags = SwapChainFlags.AllowModeSwitch
            )

let (_, device, swapChain) = Device.CreateWithSwapChain(DriverType.Hardware, DeviceCreationFlags.None, desc)
Render.Device.set device

let factory = swapChain.GetParent<Factory>()
factory.SetWindowAssociation(form.Handle, WindowAssociationFlags.IgnoreAll) |> ignore

let backBuffer = Texture2D.FromSwapChain<Texture2D>(swapChain, 0)
let backBufferView = new RenderTargetView(device, backBuffer)

let depthBuffer = new Texture2D(device, Texture2DDescription(Width = form.ClientSize.Width, Height = form.ClientSize.Height, Format = Format.D24_UNorm_S8_UInt, ArraySize = 1, MipLevels = 1, SampleDescription = SampleDescription(1, 0), BindFlags = BindFlags.DepthStencil))
let depthBufferView = new DepthStencilView(device, depthBuffer)

let basic_textured = Effect(device, "src/shaders/basic_textured.hlsl")

let vertex_size = (Render.VertexLayouts.get Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed).size
let layout = new InputLayout(device, basic_textured.VertexSignature, (Render.VertexLayouts.get Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed).elements)

let createBufferFromStream stream bind_flags =
    new Buffer(device, stream, BufferDescription(int stream.Length, ResourceUsage.Default, bind_flags, CpuAccessFlags.None, ResourceOptionFlags.None, 0))

let createRenderVertexBuffer (vertices: byte array) =
    use stream = new DataStream(vertices, canRead = false, canWrite = false)

    createBufferFromStream stream BindFlags.VertexBuffer

let createRenderIndexBuffer (indices: int array) =
    use stream = new DataStream(2L * indices.LongLength, canRead = false, canWrite = true)

    stream.WriteRange(indices |> Array.map (fun i -> uint16 i))
    stream.Position <- 0L

    createBufferFromStream stream BindFlags.IndexBuffer

let renderMeshes =
    let index_offsets = meshes |> Array.map (fun (mesh, material, skeleton, transform) -> mesh.vertices.Length / mesh.vertex_size) |> Array.scan (+) 0
    let indices = meshes |> Array.mapi (fun index (mesh, material, skeleton, transform) -> Array.map ((+) index_offsets.[index]) mesh.indices) |> Array.collect id

    let posttl = Build.Geometry.PostTLAnalyzer.analyzeFIFO indices 16

    printfn "%d triangles, %d vertices, ACMR %f, ATVR %f" (indices.Length / 3) index_offsets.[index_offsets.Length - 1] posttl.acmr posttl.atvr

    meshes |> Array.map (fun (mesh, material, skeleton, transform) ->
        mesh, material, skeleton, transform, createRenderVertexBuffer mesh.vertices, createRenderIndexBuffer mesh.indices)

let projection = Math.Camera.projectionPerspective 45.f (float32 form.ClientSize.Width / float32 form.ClientSize.Height) 1.f 1000.f
let view = Math.Camera.lookAt (Math.Vector3(0.f, 3.5f, 2.f)) (Math.Vector3(0.f, 0.f, 1.5f)) Math.Vector3.UnitZ
let view_projection = projection * Matrix44(view)

let constantBuffer0 = new Buffer(device, null, BufferDescription(16448, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0))
let constantBuffer1 = new Buffer(device, null, BufferDescription(65536, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0))

let contextHolder = new ObjectPool(fun _ -> new DeviceContext(device))

let createDummyTexture color =
    let stream = new DataStream(4L, canRead = false, canWrite = true)
    stream.WriteRange(color)
    stream.Position <- 0L

    let desc = Texture2DDescription(Width = 1, Height = 1, Format = Format.R8G8B8A8_UNorm, ArraySize = 1, MipLevels = 1, SampleDescription = SampleDescription(1, 0), BindFlags = BindFlags.ShaderResource)
    let texture = new Texture2D(device, desc, DataRectangle(4, stream))

    new ShaderResourceView(device, texture)

let dummy_albedo = createDummyTexture [|128uy; 128uy; 128uy; 255uy|]
let dummy_normal = createDummyTexture [|128uy; 128uy; 255uy; 255uy|]
let dummy_specular = createDummyTexture [|0uy; 0uy; 0uy; 0uy|]

let draw (context: DeviceContext) =
    context.Rasterizer.SetViewports(new Viewport(0.f, 0.f, float32 form.ClientSize.Width, float32 form.ClientSize.Height))
    context.OutputMerger.SetTargets(depthBufferView, backBufferView)
    context.OutputMerger.DepthStencilState <- DepthStencilState.FromDescription(device, DepthStencilStateDescription(IsDepthEnabled = true, DepthWriteMask = DepthWriteMask.All, DepthComparison = Comparison.Less))

    let fill_mode = if dbg_wireframe.Value then FillMode.Wireframe else FillMode.Solid
    context.Rasterizer.State <- RasterizerState.FromDescription(device, RasterizerStateDescription(CullMode = CullMode.Back, FillMode = fill_mode, IsFrontCounterclockwise = true))

    context.InputAssembler.InputLayout <- layout
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList

    context.VertexShader.Set(basic_textured.VertexShader)
    context.PixelShader.Set(basic_textured.PixelShader)

    context.VertexShader.SetConstantBuffer(constantBuffer0, 0)
    context.VertexShader.SetConstantBuffer(constantBuffer1, 1)
    context.PixelShader.SetSampler(SamplerState.FromDescription(device, SamplerDescription(AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap, Filter = dbg_texfilter.Value, MaximumAnisotropy = 16)), 0)

    let drawInstanced offsets =
        let box = context.MapSubresource(constantBuffer1, 0, constantBuffer1.Description.SizeInBytes, MapMode.WriteDiscard, MapFlags.None)
        box.Data.WriteRange(offsets)
        context.UnmapSubresource(constantBuffer1, 0)

        for mesh, material, skeleton, transform, vb, ib in renderMeshes do
            let setTexture (tex: Render.Texture option) dummy reg =
                context.PixelShader.SetShaderResource(tex |> Option.fold (fun _ t -> t.View) dummy, reg)

            setTexture material.albedo_map dummy_albedo 0
            setTexture material.normal_map dummy_normal 1
            setTexture material.specular_map dummy_specular 2

            let compression_info = mesh.compression_info

            let box = context.MapSubresource(constantBuffer0, 0, constantBuffer0.Description.SizeInBytes, MapMode.WriteDiscard, MapFlags.None)
            box.Data.Write(view_projection)
            box.Data.Write(compression_info.position_offset)
            box.Data.Write(0.f)
            box.Data.Write(compression_info.position_scale)
            box.Data.Write(0.f)
            box.Data.Write(compression_info.texcoord_offset)
            box.Data.Write(compression_info.texcoord_scale)
            if mesh.skin.IsSome then
                box.Data.WriteRange(mesh.skin.Value.ComputeBoneTransforms skeleton)
            else
                box.Data.Write(transform)
            context.UnmapSubresource(constantBuffer0, 0)

            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vb, vertex_size, 0))
            context.InputAssembler.SetIndexBuffer(ib, Format.R16_UInt, 0)
            context.DrawIndexedInstanced(mesh.indices.Length, offsets.Length, 0, 0, 0)

    if not dbg_stress_test.Value then
        drawInstanced [| Matrix34.Identity |]
    else
        let rng = System.Random(123456789)

        let offsets = [|0 .. 1000|] |> Array.map (fun _ ->
            let x = -20.f + 40.f * float32 (rng.NextDouble())
            let y = -25.f + 40.f * float32 (rng.NextDouble())
            Matrix34.Translation(x, 7.f, y) * Matrix34.Scaling(0.1f, 0.1f, 0.1f))

        drawInstanced offsets

    context.FinishCommandList(false)

form.KeyDown.Add(fun args ->
    if args.Alt && args.KeyCode = System.Windows.Forms.Keys.Oemcomma then
        let w = WinUI.PropertyTree.create (Core.DbgVars.getVariables() |> Array.map (fun (name, v) -> name, box v))
        w.KeyUp.Add (fun args -> if args.Key = System.Windows.Input.Key.Escape then w.Close())
        w.Show())

MessagePump.Run(form, fun () ->
    form.Text <- dbg_name.Value
    device.ImmediateContext.ClearRenderTargetView(backBufferView, SlimDX.Color4 Color.Black)
    device.ImmediateContext.ClearDepthStencilView(depthBufferView, DepthStencilClearFlags.Depth, 1.f, 0uy)

    let context = contextHolder.get()
    use cl = draw context
    contextHolder.put(context)

    device.ImmediateContext.ExecuteCommandList(cl, false)

    swapChain.Present(dbg_present_interval.Value, PresentFlags.None) |> ignore
)

device.ImmediateContext.ClearState()
device.ImmediateContext.Flush()
