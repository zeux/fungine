module main

open System
open System.Collections.Generic
open System.Drawing
open System.IO
open System.Text
open System.Windows.Forms
open SlimDX
open SlimDX.DXGI
open SlimDX.Windows
open SlimDX.D3DCompiler
open SlimDX.Direct3D11

open Core.Data

[<STAThread>]
do()

let firsttime = lazy ((System.DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds |> printfn "--- first frame latency: %.2f sec")

// initial setup
System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo.InvariantCulture
System.Environment.CurrentDirectory <- System.AppDomain.CurrentDomain.BaseDirectory + "/../.."
System.Console.WindowWidth <- max System.Console.WindowWidth 140

// build assets
assets.context.Run()

// start asset watcher
assets.watcher.Force() |> ignore

let dbg_nulldraw = Core.DbgVar(false, "render/null draw")
let dbg_wireframe = Core.DbgVar(false, "render/wireframe")
let dbg_present_interval = Core.DbgVar(0, "vsync interval")
let dbg_name = Core.DbgVar("foo", "name")
let dbg_texfilter = Core.DbgVar(Filter.Anisotropic, "render/texture/filter")
let dbg_fov = Core.DbgVar(45.f, "camera/fov")

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

let (_, device, swapChain) = Device.CreateWithSwapChain(DriverType.Hardware, DeviceCreationFlags.Debug, desc)

// setup asset loaders
AssetDB.addType "dds" (Render.TextureLoader.load device)
AssetDB.addType "mesh" (fun path -> (Core.Serialization.Load.fromFileEx path device) :?> Render.Mesh)
AssetDB.addType "shader" (fun path -> (Core.Serialization.Load.fromFileEx path device) :?> Render.Shader)

let factory = swapChain.GetParent<Factory>()
factory.SetWindowAssociation(form.Handle, WindowAssociationFlags.IgnoreAll) |> ignore

let backBuffer = Texture2D.FromSwapChain<Texture2D>(swapChain, 0)
let backBufferView = new RenderTargetView(device, backBuffer)

let sample = SampleDescription(8, 0)

let colorBuffer = new Texture2D(device, Texture2DDescription(Width = form.ClientSize.Width, Height = form.ClientSize.Height, Format = Format.R8G8B8A8_UNorm, ArraySize = 1, MipLevels = 1, SampleDescription = sample, BindFlags = BindFlags.RenderTarget))
let colorBufferView = new RenderTargetView(device, colorBuffer)

let depthBuffer = new Texture2D(device, Texture2DDescription(Width = form.ClientSize.Width, Height = form.ClientSize.Height, Format = Format.D24_UNorm_S8_UInt, ArraySize = 1, MipLevels = 1, SampleDescription = sample, BindFlags = BindFlags.DepthStencil))
let depthBufferView = new DepthStencilView(device, depthBuffer)

let basic_textured = Asset<Render.Shader>.Load ".build/src/shaders/basic_textured.shader"

let vertex_size = (Render.VertexLayouts.get Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed).size
let layout = new InputLayout(device, basic_textured.Data.VertexSignature.Resource, (Render.VertexLayouts.get Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed).elements)

let constantBuffer0 = new Buffer(device, null, BufferDescription(16448, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0))
let constantBuffer1 = new Buffer(device, null, BufferDescription(65536, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0))

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

let mouse = Input.Mouse(form)
let keyboard = Input.Keyboard(form)
let camera_controller = Camera.CameraController(mouse, keyboard, Position = Vector3(-7.100705f, 47.303590f, 22.963710f), Yaw = -1.3f, Pitch = 0.15f)

let scene = List<Asset<Render.Mesh> * Matrix34>()

let placeMesh path parent =
    try
        let mesh = Asset.LoadAsync (".build/art/" + System.IO.Path.ChangeExtension(path, ".mesh"))
        scene.Add((mesh, parent))
    with e -> printfn "%s" e.Message

let placeFile path parent =
    let nodes =
        let doc = Core.Data.Load.fromFile path
        doc.Pairs |> dict

    let parseTransform node =
        match node with
        | Array elements ->
            let v = elements |> Array.map (function | Value n -> float32 n | _ -> failwith "Incorrect transform element")
            assert (v.Length = 12)
            Matrix34(v.[0], v.[1], v.[2], v.[3], v.[4], v.[5], v.[6], v.[7], v.[8], v.[9], v.[10], v.[11])
        | _ -> failwith "Incorrect transform value"
        
    let rec placeNode node parent =
        match node with
        | Object list ->
            list
            |> Array.fold
                (fun parent (name, value) ->
                    match name with
                    | "transform" ->
                        parent * parseTransform value
                    | "node" ->
                        placeNode value parent
                        parent
                    | "mesh" ->
                        match value with
                        | Object [|"path", Value path|] -> placeMesh path parent
                        | _ -> failwith "Incorrect mesh node"
                        parent
                    | "reference" ->
                        match value with
                        | Object [|"name", Value name|] -> placeNode nodes.[name] parent
                        | _ -> failwith "Incorrect reference node"
                        parent
                    | _ -> parent) parent
            |> ignore
        | _ -> failwith "Expected object"

    let root = Path.GetFileNameWithoutExtension(path)

    placeNode nodes.[root + "_" + root] parent

let scene_name = "heaven"
// placeFile (sprintf "art/%s/%s.world" scene_name scene_name) Matrix34.Identity

placeMesh "slave_driver/cc_slave_driver.mesh" (Matrix34.Translation(-6.f, -2.f, 0.f) * Matrix34.Scaling(0.07f, 0.07f, 0.07f))
placeMesh "heaven/meshes/buildings/lab.mesh" Matrix34.Identity
placeMesh "heaven/meshes/buildings/lab_gears.mesh" Matrix34.Identity

let rng = System.Random()

for x = -10 to 10 do
    for y = -10 to 10 do
        placeMesh (sprintf "floor/floor%02d.mesh" (rng.Next(1, 15))) (Matrix34.Translation(float32 x * 2.f, float32 y * 2.f, 0.f))

let dbg_smoothness = Core.DbgVar(0.f, "lighting/smoothness")
let dbg_roughness = Core.DbgVar(0.5f, "lighting/roughness")

let draw (context: DeviceContext) =
    let projection = Math.Camera.projectionPerspective (dbg_fov.Value / 180.f * float32 Math.PI) (float32 form.ClientSize.Width / float32 form.ClientSize.Height) 0.1f 1000.f
    let view = camera_controller.ViewMatrix
    let view_projection = projection * Matrix44(view)

    context.Rasterizer.SetViewports(new Viewport(0.f, 0.f, float32 form.ClientSize.Width, float32 form.ClientSize.Height))
    context.OutputMerger.SetTargets(depthBufferView, colorBufferView)
    context.OutputMerger.DepthStencilState <- DepthStencilState.FromDescription(device, DepthStencilStateDescription(IsDepthEnabled = true, DepthWriteMask = DepthWriteMask.All, DepthComparison = Comparison.Less))

    let fill_mode = if dbg_wireframe.Value then FillMode.Wireframe else FillMode.Solid
    context.Rasterizer.State <- RasterizerState.FromDescription(device, RasterizerStateDescription(CullMode = CullMode.Back, FillMode = fill_mode, IsFrontCounterclockwise = true))

    context.InputAssembler.InputLayout <- layout
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList

    basic_textured.Data.Set(context)

    context.VertexShader.SetConstantBuffer(constantBuffer0, 0)
    context.VertexShader.SetConstantBuffer(constantBuffer1, 1)
    context.PixelShader.SetConstantBuffer(constantBuffer0, 0)
    context.PixelShader.SetSampler(SamplerState.FromDescription(device, SamplerDescription(AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap, Filter = dbg_texfilter.Value, MaximumAnisotropy = 16, MaximumLod = infinityf)), 0)

    for mesh, instances in scene |> Seq.choose (fun (mesh, transform) -> if mesh.IsReady then Some (mesh.Data, transform) else None) |> Seq.groupBy (fun (mesh, transform) -> mesh) do
        let transforms = instances |> Seq.toArray |> Array.map (fun (mesh, transform) -> transform)

        if dbg_nulldraw.Value then () else
        let box = context.MapSubresource(constantBuffer1, 0, constantBuffer1.Description.SizeInBytes, MapMode.WriteDiscard, MapFlags.None)
        box.Data.WriteRange(transforms)
        context.UnmapSubresource(constantBuffer1, 0)

        for fragment in mesh.fragments do
            let setTexture (tex: Asset<Render.Texture> option) dummy reg =
                context.PixelShader.SetShaderResource((if tex.IsSome && tex.Value.IsReady then tex.Value.Data.View else dummy), reg)

            let material = fragment.material

            setTexture material.albedo_map dummy_albedo 0
            setTexture material.normal_map dummy_normal 1
            setTexture material.specular_map dummy_specular 2

            let compression_info = fragment.compression_info

            let box = context.MapSubresource(constantBuffer0, 0, constantBuffer0.Description.SizeInBytes, MapMode.WriteDiscard, MapFlags.None)
            box.Data.Write(view_projection)
            box.Data.Write(camera_controller.Position)
            box.Data.Write(dbg_roughness.Value)
            box.Data.Write(compression_info.pos_offset)
            box.Data.Write(dbg_smoothness.Value)
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
        w.Width <- 400.0
        w.KeyUp.Add (fun args -> if args.Key = System.Windows.Input.Key.Escape then w.Close())
        w.Show())

let frame_timer = Diagnostics.Stopwatch.StartNew()

MessagePump.Run(form, fun () ->
    let dt = float32 frame_timer.Elapsed.TotalSeconds
    frame_timer.Restart()

    mouse.Update()
    keyboard.Update()
    camera_controller.Update(dt)

    device.ImmediateContext.ClearRenderTargetView(colorBufferView, SlimDX.Color4 Color.Gray)
    device.ImmediateContext.ClearDepthStencilView(depthBufferView, DepthStencilClearFlags.Depth, 1.f, 0uy)

    draw device.ImmediateContext

    device.ImmediateContext.ResolveSubresource(colorBuffer, 0, backBuffer, 0, Format.R8G8B8A8_UNorm)

    swapChain.Present(dbg_present_interval.Value, PresentFlags.None) |> ignore

    firsttime.Force() |> ignore)

device.ImmediateContext.ClearState()
device.ImmediateContext.Flush()
