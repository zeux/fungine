module main

open System
open System.Collections.Generic
open System.Drawing
open System.Windows.Forms
open SlimDX
open SlimDX.DXGI
open SlimDX.Windows
open SlimDX.D3DCompiler
open SlimDX.Direct3D11

[<STAThread>]
do()

Core.Test.run ()

let dbg_wireframe = Core.DbgVar(false, "render/wireframe")
let dbg_present_interval = Core.DbgVar(0, "vsync interval")
let dbg_name = Core.DbgVar("foo", "name")
let dbg_fillmode = Core.DbgVar(FillMode.Solid, "render/fill mode")
let dbg_texfilter = Core.DbgVar(Filter.Anisotropic, "render/texture/filter")
let dbg_fov = Core.DbgVar(45.f, "camera/fov")

System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo.InvariantCulture
System.Environment.CurrentDirectory <- System.IO.Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "/../.."
System.Console.WindowWidth <- max System.Console.WindowWidth 140

assets.buildMeshes "art"

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

let form = new Form(Text = "fungine", Width = 1280, Height = 720)
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

let camera_controller = Camera.CameraController()

let meshes = Core.Cache(fun path -> (Core.Serialization.Load.fromFile path) :?> Render.Mesh)
let scene = List<Render.Mesh * Matrix34>()

let placeMesh path transform =
    try
        let mesh = meshes.Get (".build/art/" + System.IO.Path.ChangeExtension(path, ".mesh"))
        let fixup = Matrix34(Vector4.UnitX, Vector4.UnitZ, Vector4.UnitY)
        scene.Add((mesh, transform))
    with e -> printfn "%A" e

Scene.heaven_heaven placeMesh Matrix34.Identity

let draw (context: DeviceContext) =
    let projection = Math.Camera.projectionPerspective (dbg_fov.Value / 180.f * float32 Math.PI) (float32 form.ClientSize.Width / float32 form.ClientSize.Height) 0.01f 1000.f
    let view = camera_controller.ViewMatrix
    let view_projection = projection * Matrix44(view)

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
    context.PixelShader.SetSampler(SamplerState.FromDescription(device, SamplerDescription(AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap, Filter = dbg_texfilter.Value, MaximumAnisotropy = 16, MaximumLod = infinityf)), 0)

    for mesh, instances in scene |> Seq.groupBy (fun (mesh, transform) -> mesh) do
        let transforms = instances |> Seq.toArray |> Array.map (fun (mesh, transform) -> transform)

        let box = context.MapSubresource(constantBuffer1, 0, constantBuffer1.Description.SizeInBytes, MapMode.WriteDiscard, MapFlags.None)
        box.Data.WriteRange(transforms)
        context.UnmapSubresource(constantBuffer1, 0)

        for fragment in mesh.fragments do
            let setTexture (tex: Render.Texture option) dummy reg =
                context.PixelShader.SetShaderResource(tex |> Option.fold (fun _ t -> t.View) dummy, reg)

            let material = fragment.material

            setTexture material.albedo_map dummy_albedo 0
            setTexture material.normal_map dummy_normal 1
            setTexture material.specular_map dummy_specular 2

            let compression_info = fragment.compression_info

            let box = context.MapSubresource(constantBuffer0, 0, constantBuffer0.Description.SizeInBytes, MapMode.WriteDiscard, MapFlags.None)
            box.Data.Write(view_projection)
            box.Data.Write(compression_info.pos_offset)
            box.Data.Write(0.f)
            box.Data.Write(compression_info.pos_scale)
            box.Data.Write(0.f)
            box.Data.Write(compression_info.uv_offset)
            box.Data.Write(compression_info.uv_scale)
            box.Data.WriteRange(fragment.skin.ComputeBoneTransforms mesh.skeleton)
            context.UnmapSubresource(constantBuffer0, 0)

            context.InputAssembler.SetVertexBuffers(0, VertexBufferBinding(mesh.vertices.Resource, vertex_size, fragment.vertex_offset))
            context.InputAssembler.SetIndexBuffer(mesh.indices.Resource, fragment.index_format, fragment.index_offset)
            context.DrawIndexedInstanced(fragment.index_count, transforms.Length, 0, 0, 0)

form.KeyUp.Add(fun args ->
    if args.Alt && args.KeyCode = System.Windows.Forms.Keys.Oemcomma then
        let w = WinUI.PropertyGrid.create (Core.DbgVars.getVariables() |> Array.map (fun (name, v) -> name, box v))
        w.KeyUp.Add (fun args -> if args.Key = System.Windows.Input.Key.Escape then w.Close())
        w.Show())

MessagePump.Run(form, fun () ->
    camera_controller.Update(1.f / 60.f)

    form.Text <- dbg_name.Value
    device.ImmediateContext.ClearRenderTargetView(backBufferView, SlimDX.Color4 Color.Gray)
    device.ImmediateContext.ClearDepthStencilView(depthBufferView, DepthStencilClearFlags.Depth, 1.f, 0uy)

    draw device.ImmediateContext

    swapChain.Present(dbg_present_interval.Value, PresentFlags.None) |> ignore
)

device.ImmediateContext.ClearState()
device.ImmediateContext.Flush()
