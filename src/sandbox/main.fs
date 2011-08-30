module main

open System
open System.Collections.Generic
open System.Drawing
open System.IO
open System.Windows.Forms
open SlimDX
open SlimDX.DXGI
open SlimDX.Windows
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
let device = Render.Device(form.Handle)
let rtpool = Render.RenderTargetPool(device.Device)

// setup onsize handler
form.SizeChanged.Add(fun args ->
    device.OnSizeChanged ()
    rtpool.ReleaseUnused())

// setup asset loaders
AssetDB.addType "dds" (Render.TextureLoader.load device.Device)
AssetDB.addType "mesh" (fun path -> (Core.Serialization.Load.fromFileEx path device.Device) :?> Render.Mesh)
AssetDB.addType "shader" (fun path -> (Core.Serialization.Load.fromFileEx path device.Device) :?> Render.Shader)

let gbuffer_fill = Asset<Render.Shader>.Load ".build/src/shaders/gbuffer_fill_default.shader"
let light_directional = Asset<Render.Shader>.Load ".build/src/shaders/lighting/directional.shader"
let light_spot = Asset<Render.Shader>.Load ".build/src/shaders/lighting/spot.shader"
let postfx_tonemap = Asset<Render.Shader>.Load ".build/src/shaders/postfx/tonemap.shader"
let postfx_fxaa = Asset<Render.Shader>.Load ".build/src/shaders/postfx/fxaa.shader"

let vertex_size = (Render.VertexLayouts.get Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed).size
let layout = new InputLayout(device.Device, gbuffer_fill.Data.VertexSignature.Resource, (Render.VertexLayouts.get Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed).elements)

let constantBuffer0 = new Buffer(device.Device, null, BufferDescription(16512, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0))
let constantBuffer1 = new Buffer(device.Device, null, BufferDescription(65536, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0))

let createDummyTexture color =
    let stream = new DataStream(4L, canRead = false, canWrite = true)
    stream.WriteRange(color)
    stream.Position <- 0L

    let desc = Texture2DDescription(Width = 1, Height = 1, Format = Format.R8G8B8A8_UNorm, ArraySize = 1, MipLevels = 1, SampleDescription = SampleDescription(1, 0), BindFlags = BindFlags.ShaderResource)
    let texture = new Texture2D(device.Device, desc, DataRectangle(4, stream))

    new ShaderResourceView(device.Device, texture)

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

placeMesh "slave_driver/cc_slave_driver.mesh" (Matrix34.Translation(10.f-6.f, 10.f-2.f, 0.f) * Matrix34.Scaling(0.07f, 0.07f, 0.07f))
placeMesh "heaven/meshes/buildings/lab.mesh" Matrix34.Identity
placeMesh "heaven/meshes/buildings/lab_gears.mesh" Matrix34.Identity

let rng = System.Random()

let size = 50
for x = -size to size do
    for y = -size to size do
        placeMesh (sprintf "floor/floor%02d.mesh" (rng.Next(1, 15))) (Matrix34.Translation(float32 x * 2.f, float32 y * 2.f, 0.f))

let dbg_smoothness = Core.DbgVar(0.f, "lighting/smoothness")
let dbg_roughness = Core.DbgVar(0.5f, "lighting/roughness")

module Seq =
    let groupByRef sel items =
        let dict = Dictionary<_, List<_>>(HashIdentity.Reference)

        for i in items do
            let key = sel i
            let mutable l = null
            match dict.TryGetValue(key, &l) with
            | true -> l.Add(i)
            | _ ->
                let l = List<_>()
                l.Add(i)
                dict.Add(key, l)

        dict |> Seq.map (fun p -> p.Key, p.Value.ToArray())

[<Render.ShaderStruct>]
type Camera(view: Matrix34, projection: Matrix44) =
    member this.View = view
    member this.Projection = projection
    member this.ViewProjection = projection * Matrix44(view)
    member this.ViewProjectionInverse = Matrix44.Inverse(this.ViewProjection)
    member this.EyePosition = view.Column 3

[<Render.ShaderStruct>]
type SpotLight(position: Vector3, direction: Vector3, outer_angle: float32, inner_angle: float32, radius: float32, color: Vector3) =
    member this.Position = position
    member this.Direction = direction
    member this.OuterAngle = outer_angle
    member this.InnerAngle = inner_angle
    member this.Radius = radius
    member this.Color = color

let uploadConstantData (context: DeviceContext) data =
    let size, upload = Render.ShaderStruct.getUploadDelegate (data.GetType())
    let cb = new Buffer(device.Device, null, BufferDescription(size, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0))
    let box = context.MapSubresource(cb, MapMode.WriteDiscard, MapFlags.None)
    upload.Invoke(data, box.Data.DataPointer, size)
    context.UnmapSubresource(cb, 0)
    cb

type ShaderContext(context: DeviceContext) =
    let values = List<obj>()
    let vertex_params = List<Render.ShaderParameter>()
    let pixel_params = List<Render.ShaderParameter>()

    member private this.EnsureSlot(slot) =
        while values.Count <= slot do
            values.Add(null)
            vertex_params.Add(Render.ShaderParameter())
            pixel_params.Add(Render.ShaderParameter())

    member private this.ValidateSlot(slot) =
        let inline bind (p: Render.ShaderParameter) (value: obj) (stage: ^T) =
            match p.Binding with
            | Render.ShaderParameterBinding.None -> ()
            | Render.ShaderParameterBinding.ConstantBuffer -> (^T: (member SetConstantBuffer: _ -> _ -> _) (stage, value :?> Buffer, p.Register))
            | Render.ShaderParameterBinding.ShaderResource -> (^T: (member SetShaderResource: _ -> _ -> _) (stage, value :?> ShaderResourceView, p.Register))
            | Render.ShaderParameterBinding.Sampler -> (^T: (member SetSampler: _ -> _ -> _) (stage, value :?> SamplerState, p.Register))
            | x -> failwithf "Unexpected binding value %A" x

        bind vertex_params.[slot] values.[slot] context.VertexShader
        bind pixel_params.[slot] values.[slot] context.PixelShader

    member private this.UpdateSlot(slot, value) =
        this.EnsureSlot(slot)
        values.[slot] <- value
        this.ValidateSlot(slot)

    member this.Shader
        with set (value: Render.Shader) =
            context.VertexShader.Set(value.VertexShader.Resource)
            context.PixelShader.Set(value.PixelShader.Resource)

            for slot = 0 to values.Count - 1 do
                vertex_params.[slot] <- Render.ShaderParameter()
                pixel_params.[slot] <- Render.ShaderParameter()

            for p in value.VertexShader.Parameters do
                this.EnsureSlot(p.Slot)
                vertex_params.[p.Slot] <- p

            for p in value.PixelShader.Parameters do
                this.EnsureSlot(p.Slot)
                pixel_params.[p.Slot] <- p

            for slot = 0 to values.Count - 1 do
                if values.[slot] <> null then
                    this.ValidateSlot(slot)

    member this.SetConstant(slot, value: ShaderResourceView) =
        this.UpdateSlot(slot, value)

    member this.SetConstant(slot, value: SamplerState) =
        this.UpdateSlot(slot, value)

    member this.SetConstant(slot, value: Buffer) =
        this.UpdateSlot(slot, value)

    member this.SetConstant(slot, value: obj) =
        this.UpdateSlot(slot, uploadConstantData context value)

    static member (?<-) (this: ShaderContext, name: string, value: ShaderResourceView) =
        this.SetConstant(Render.ShaderParameterRegistry.getSlot name, value)

    static member (?<-) (this: ShaderContext, name: string, value: SamplerState) =
        this.SetConstant(Render.ShaderParameterRegistry.getSlot name, value)

    static member (?<-) (this: ShaderContext, name: string, value: Buffer) =
        this.SetConstant(Render.ShaderParameterRegistry.getSlot name, value)

    static member (?<-) (this: ShaderContext, name: string, value: obj) =
        this.SetConstant(Render.ShaderParameterRegistry.getSlot name, value)

let renderScene (context: DeviceContext) (shader_context: ShaderContext) (camera: Camera) =
    context.OutputMerger.DepthStencilState <- DepthStencilState.FromDescription(device.Device, DepthStencilStateDescription(IsDepthEnabled = true, DepthWriteMask = DepthWriteMask.All, DepthComparison = Comparison.Less))

    let fill_mode = if dbg_wireframe.Value then FillMode.Wireframe else FillMode.Solid
    context.Rasterizer.State <- RasterizerState.FromDescription(device.Device, RasterizerStateDescription(CullMode = CullMode.Back, FillMode = fill_mode, IsFrontCounterclockwise = true))

    context.InputAssembler.InputLayout <- layout
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList

    shader_context.Shader <- gbuffer_fill.Data

    shader_context?camera <- camera
    shader_context?default_sampler <- SamplerState.FromDescription(device.Device, SamplerDescription(AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap, Filter = dbg_texfilter.Value, MaximumAnisotropy = 16, MaximumLod = infinityf))
    shader_context?mesh <- constantBuffer0
    shader_context?transforms <- constantBuffer1

    for mesh, instances in scene |> Seq.choose (fun (mesh, transform) -> if mesh.IsReady then Some (mesh.Data, transform) else None) |> Seq.groupByRef (fun (mesh, transform) -> mesh) do
        let transforms = instances |> Array.map (fun (mesh, transform) -> transform)

        if dbg_nulldraw.Value then () else
        let box = context.MapSubresource(constantBuffer1, MapMode.WriteDiscard, MapFlags.None)
        box.Data.WriteRange(transforms)
        context.UnmapSubresource(constantBuffer1, 0)

        for fragment in mesh.fragments do
            let setTexture (tex: Asset<Render.Texture> option) dummy name =
                shader_context?(name) <- (if tex.IsSome && tex.Value.IsReady then tex.Value.Data.View else dummy)

            let material = fragment.material

            setTexture material.albedo_map dummy_albedo "albedo_map"
            setTexture material.normal_map dummy_normal "normal_map"
            setTexture material.specular_map dummy_specular "specular_map"

            let compression_info = fragment.compression_info

            let box = context.MapSubresource(constantBuffer0, MapMode.WriteDiscard, MapFlags.None)
            box.Data.Write(dbg_roughness.Value)
            box.Data.Write(compression_info.pos_offset)
            box.Data.Write(dbg_smoothness.Value)
            box.Data.Write(compression_info.pos_scale)
            box.Data.Write(compression_info.uv_offset)
            box.Data.Write(compression_info.uv_scale)
            box.Data.WriteRange(fragment.skin.ComputeBoneTransforms mesh.skeleton)
            context.UnmapSubresource(constantBuffer0, 0)

            context.InputAssembler.SetVertexBuffers(0, VertexBufferBinding(mesh.vertices.Resource, vertex_size, fragment.vertex_offset))
            context.InputAssembler.SetIndexBuffer(mesh.indices.Resource, fragment.index_format, fragment.index_offset)
            context.DrawIndexedInstanced(fragment.index_count, transforms.Length, 0, 0, 0)

let fillGBuffer (context: DeviceContext) (shader_context: ShaderContext) (camera: Camera) (albedoBuffer: Render.RenderTarget) (specBuffer: Render.RenderTarget) (normalBuffer: Render.RenderTarget) (depthBuffer: Render.RenderTarget) =
    context.Rasterizer.SetViewports(new Viewport(0.f, 0.f, float32 form.ClientSize.Width, float32 form.ClientSize.Height))
    context.OutputMerger.SetTargets(depthBuffer.DepthView, [|albedoBuffer; specBuffer; normalBuffer|] |> Array.map (fun rt -> rt.ColorView))

    Performance.BeginEvent(Color4(), "gbuffer") |> ignore
    renderScene context shader_context camera
    Performance.EndEvent() |> ignore

let fillShadowBuffer (context: DeviceContext) (shader_context: ShaderContext) (camera: Camera) (shadowBuffer: Render.RenderTarget) =
    let texture = shadowBuffer.Resource :?> Texture2D
    context.Rasterizer.SetViewports(new Viewport(0.f, 0.f, float32 texture.Description.Width, float32 texture.Description.Height))

    use dummyBuffer = rtpool.Acquire("dummy", texture.Description.Width, texture.Description.Height, Format.R8G8B8A8_UNorm)

    context.ClearRenderTargetView(dummyBuffer.ColorView, Color4())

    context.OutputMerger.SetTargets(shadowBuffer.DepthView, dummyBuffer.ColorView)

    Performance.BeginEvent(Color4(), "shadowbuffer") |> ignore
    renderScene context shader_context camera
    Performance.EndEvent() |> ignore

form.KeyUp.Add(fun args ->
    if args.Alt && args.KeyCode = System.Windows.Forms.Keys.Oemcomma then
        let w = WinUI.PropertyGrid.create (Core.DbgVars.getVariables() |> Array.map (fun (name, v) -> name, box v))
        w.Width <- 400.0
        w.KeyUp.Add (fun args -> if args.Key = System.Windows.Input.Key.Escape then w.Close())
        w.Show())

let frame_timer = Diagnostics.Stopwatch.StartNew()

let dbg_spot_offset = Core.DbgVar(30.f, "spot/offset")
let dbg_spot_height = Core.DbgVar(30.f, "spot/height")
let dbg_spot_innercone = Core.DbgVar(19.f, "spot/inner cone")
let dbg_spot_outercone = Core.DbgVar(20.f, "spot/outer cone")
let dbg_spot_radius = Core.DbgVar(100.f, "spot/radius")
let dbg_spot_intensity = Core.DbgVar(8.f, "spot/intensity")
let dbg_spot_seed = Core.DbgVar(1, "spot/seed")
let dbg_spot_count = Core.DbgVar(3, "spot/count")

MessagePump.Run(form, fun () ->
    let dt = float32 frame_timer.Elapsed.TotalSeconds
    frame_timer.Restart()

    mouse.Update()
    keyboard.Update()
    camera_controller.Update(dt)

    let context = device.Device.ImmediateContext
    let shader_context = ShaderContext(context)

    use colorBuffer = rtpool.Acquire("scene/hdr", form.ClientSize.Width, form.ClientSize.Height, Format.R16G16B16A16_Float)
    use depthBuffer = rtpool.Acquire("gbuffer/depth", form.ClientSize.Width, form.ClientSize.Height, Format.D24_UNorm_S8_UInt)

    // main camera
    let camera = Camera(camera_controller.ViewMatrix, Math.Camera.projectionPerspective (dbg_fov.Value / 180.f * float32 Math.PI) (float32 form.ClientSize.Width / float32 form.ClientSize.Height) 0.1f 1000.f)

    // fill gbuffer
    use albedoBuffer = rtpool.Acquire("gbuffer/albedo", form.ClientSize.Width, form.ClientSize.Height, Format.R8G8B8A8_UNorm)
    use specularBuffer = rtpool.Acquire("gbuffer/specular", form.ClientSize.Width, form.ClientSize.Height, Format.R8G8B8A8_UNorm)
    use normalBuffer = rtpool.Acquire("gbuffer/normal", form.ClientSize.Width, form.ClientSize.Height, Format.R16G16B16A16_UNorm)

    context.ClearRenderTargetView(albedoBuffer.ColorView, SlimDX.Color4 Color.Gray)
    context.ClearRenderTargetView(specularBuffer.ColorView, SlimDX.Color4 Color.Black)
    context.ClearRenderTargetView(normalBuffer.ColorView, SlimDX.Color4 Color.Black)
    context.ClearDepthStencilView(depthBuffer.DepthView, DepthStencilClearFlags.Depth, 1.f, 0uy)

    fillGBuffer context shader_context camera albedoBuffer specularBuffer normalBuffer depthBuffer

    context.Rasterizer.SetViewports(new Viewport(0.f, 0.f, float32 form.ClientSize.Width, float32 form.ClientSize.Height))

    // light & shade
    context.OutputMerger.SetTargets(colorBuffer.ColorView)
    context.InputAssembler.InputLayout <- null
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList
    context.OutputMerger.DepthStencilState <- DepthStencilState.FromDescription(device.Device, DepthStencilStateDescription(IsDepthEnabled = false))
    context.Rasterizer.State <- RasterizerState.FromDescription(device.Device, RasterizerStateDescription(CullMode = CullMode.None, FillMode = FillMode.Solid))

    shader_context.Shader <- light_directional.Data

    shader_context?gbuf_sampler <- SamplerState.FromDescription(device.Device, SamplerDescription(AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, Filter = Filter.MinMagMipLinear))
    shader_context?gbuf_albedo <- albedoBuffer.View
    shader_context?gbuf_specular <- specularBuffer.View
    shader_context?gbuf_normal <- normalBuffer.View
    shader_context?gbuf_depth <- depthBuffer.View

    context.Draw(3, 0)

    // add some spot lights!
    let mutable blendon = BlendStateDescription()
    Array.fill blendon.RenderTargets 0 8 (RenderTargetBlendDescription(BlendEnable = true, SourceBlend = BlendOption.One, DestinationBlend = BlendOption.One, BlendOperation = BlendOperation.Add, SourceBlendAlpha = BlendOption.One, DestinationBlendAlpha = BlendOption.Zero, BlendOperationAlpha = BlendOperation.Add, RenderTargetWriteMask = ColorWriteMaskFlags.All))

    let deg2rad = float32 System.Math.PI / 180.f

    let colors = [|
        Vector3(1.f, 0.f, 0.f)
        Vector3(0.f, 1.f, 0.f)
        Vector3(0.f, 0.f, 1.f)
        Vector3(1.f, 0.f, 1.f)
        Vector3(1.f, 1.f, 0.f)
        Vector3(0.f, 1.f, 1.f)
        Vector3(1.f, 1.f, 1.f) |]

    let rng = System.Random(dbg_spot_seed.Value)

    use shadowBuffer = rtpool.Acquire("lighting/shadow buffer", 1024, 1024, Format.D24_UNorm_S8_UInt)

    for i in 0 .. dbg_spot_count.Value - 1 do
        // generate procedural spot light
        let angle = deg2rad * 360.f * (float32 i / float32 dbg_spot_count.Value)
        let position = Vector3(dbg_spot_offset.Value * sin angle, dbg_spot_offset.Value * cos angle, dbg_spot_height.Value)
        let light =
            SpotLight(
                position,
                Vector3.Normalize(-position),
                cos (deg2rad * dbg_spot_outercone.Value),
                cos (deg2rad * dbg_spot_innercone.Value),
                dbg_spot_radius.Value,
                colors.[i % colors.Length] * dbg_spot_intensity.Value)

        // render shadow map
        let light_camera =
            Camera(
                Math.Camera.lookAt light.Position (light.Position + light.Direction) (if abs light.Direction.x < 0.7f then Vector3.UnitX else Vector3.UnitY),
                Math.Camera.projectionPerspective (deg2rad * dbg_spot_outercone.Value * 2.f) 1.f 0.1f light.Radius)

        context.ClearDepthStencilView(shadowBuffer.DepthView, DepthStencilClearFlags.Depth, 1.f, 0uy)

        fillShadowBuffer context shader_context light_camera shadowBuffer

        // draw spot light
        shader_context?camera <- camera

        context.Rasterizer.SetViewports(new Viewport(0.f, 0.f, float32 form.ClientSize.Width, float32 form.ClientSize.Height))
        context.OutputMerger.SetTargets(colorBuffer.ColorView)
        context.InputAssembler.InputLayout <- null
        context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList
        context.OutputMerger.DepthStencilState <- DepthStencilState.FromDescription(device.Device, DepthStencilStateDescription(IsDepthEnabled = false))
        context.Rasterizer.State <- RasterizerState.FromDescription(device.Device, RasterizerStateDescription(CullMode = CullMode.None, FillMode = FillMode.Solid))
        context.OutputMerger.BlendState <- BlendState.FromDescription(device.Device, blendon)

        shader_context.Shader <- light_spot.Data
        shader_context?light <- light
        shader_context?light_camera <- light_camera
        shader_context?shadow_map <- shadowBuffer.View
        shader_context?shadow_sampler <- SamplerState.FromDescription(device.Device, SamplerDescription(AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, Filter = Filter.ComparisonMinMagMipLinear, ComparisonFunction = Comparison.Less))

        context.Draw(3, 0)

        context.OutputMerger.BlendState <- null

    // tonemap
    use ldrBuffer = rtpool.Acquire("scene/ldr", form.ClientSize.Width, form.ClientSize.Height, Format.R8G8B8A8_UNorm)

    context.OutputMerger.SetTargets(ldrBuffer.ColorView)
    context.InputAssembler.InputLayout <- null
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList
    context.OutputMerger.DepthStencilState <- DepthStencilState.FromDescription(device.Device, DepthStencilStateDescription(IsDepthEnabled = false))
    context.Rasterizer.State <- RasterizerState.FromDescription(device.Device, RasterizerStateDescription(CullMode = CullMode.None, FillMode = FillMode.Solid))

    shader_context.Shader <- postfx_tonemap.Data

    shader_context?default_sampler <- SamplerState.FromDescription(device.Device, SamplerDescription(AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, Filter = Filter.MinMagMipLinear))
    shader_context?color_map <- colorBuffer.View

    context.Draw(3, 0)

    // fxaa blit
    context.OutputMerger.SetTargets(device.BackBuffer.ColorView)
    context.InputAssembler.InputLayout <- null
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList
    context.OutputMerger.DepthStencilState <- DepthStencilState.FromDescription(device.Device, DepthStencilStateDescription(IsDepthEnabled = false))
    context.Rasterizer.State <- RasterizerState.FromDescription(device.Device, RasterizerStateDescription(CullMode = CullMode.None, FillMode = FillMode.Solid))

    shader_context.Shader <- postfx_fxaa.Data

    shader_context?default_sampler <- SamplerState.FromDescription(device.Device, SamplerDescription(AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, Filter = Filter.MinMagMipLinear))
    shader_context?color_map <- ldrBuffer.View

    context.Draw(3, 0)

    device.SwapChain.Present(dbg_present_interval.Value, PresentFlags.None) |> ignore

    firsttime.Force() |> ignore)

device.Device.ImmediateContext.ClearState()
device.Device.ImmediateContext.Flush()
