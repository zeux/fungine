module main

open System
open System.Collections.Generic
open System.Drawing
open System.IO
open System.Windows.Forms

open SharpDX.Data
open SharpDX.DXGI
open SharpDX.Windows
open SharpDX.Direct3D11

open Core.Data

open Render.Lighting

type PixHelper = SharpDX.Direct3D.PixHelper

[<STAThread>]
do ()

SharpDX.Configuration.EnableReleaseOnFinalizer <- true

let firsttime = lazy ((System.DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds |> printfn "--- first frame latency: %.2f sec")

// initial setup
System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo.InvariantCulture
System.Environment.CurrentDirectory <- System.AppDomain.CurrentDomain.BaseDirectory + "/../.."
System.Console.WindowWidth <- max System.Console.WindowWidth 140

// build assets
assets.context.Run()

let dbgNulldraw = Core.DbgVar(false, "render/null draw")
let dbgWireframe = Core.DbgVar(false, "render/wireframe")
let dbgPresentInterval = Core.DbgVar(0, "vsync interval")
let dbgName = Core.DbgVar("foo", "name")
let dbgTexfilter = Core.DbgVar(Filter.Anisotropic, "render/texture/filter")
let dbgFov = Core.DbgVar(45.f, "camera/fov")

let form = new Form(Text = "fungine", Width = 1280, Height = 720)
let device = Render.Device(form.Handle)
let rtpool = Render.RenderTargetPool(device.Device)

// setup onsize handler
form.SizeChanged.Add(fun args ->
    device.OnSizeChanged ()
    rtpool.ReleaseUnused())

// setup asset loaders
let assetDB = Asset.Database()
let fixupContext loader = Core.Serialization.Fixup.Create (device.Device, loader)
let loader =
    Asset.Loader(assetDB,
        dict [
            ".dds", fun path l -> Render.TextureLoader.load device.Device path |> box
            ".mesh", fun path l -> (Core.Serialization.Load.fromFileEx path (fixupContext l)) :?> Render.Mesh |> box
            ".shader", fun path l -> Core.Serialization.Load.fromFileEx path (fixupContext l) |> box
        ])

// start asset watcher
let _ = assets.assetWatcher loader

let gbufferFill = loader.Load<Render.Shader> ".build/src/shaders/gbuffer_fill_default.shader"
let depthFill = loader.Load<Render.Shader> ".build/src/shaders/depth_fill_default.shader"
let postfxTonemap = loader.Load<Render.Shader> ".build/src/shaders/postfx/tonemap.shader"
let postfxFxaa = loader.Load<Render.Shader> ".build/src/shaders/postfx/fxaa.shader"
let postfxBlit = loader.Load<Render.Shader> ".build/src/shaders/postfx/blit.shader"
let lightGridDebug = loader.Load<Render.Shader> ".build/src/shaders/lighting/lightgrid_debug.shader"
let lightGridFill = loader.Load<Render.Program> ".build/src/shaders/lighting/lightgrid_fill.shader"

let vertexSize = (Render.VertexLayouts.get Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed).size
let layout = new InputLayout(device.Device, gbufferFill.Value.VertexSignature.Resource, (Render.VertexLayouts.get Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed).elements)

let createDummyTexture color =
    let stream = new DataStream(4, canRead = false, canWrite = true)
    stream.WriteRange(color)
    stream.Position <- 0L

    let desc = Texture2DDescription(Width = 1, Height = 1, Format = Format.R8G8B8A8_UNorm, ArraySize = 1, MipLevels = 1, SampleDescription = SampleDescription(1, 0), BindFlags = BindFlags.ShaderResource)
    let texture = new Texture2D(device.Device, desc, DataRectangle(stream.DataPointer, 4))

    new ShaderResourceView(device.Device, texture)

let dummyAlbedo = createDummyTexture [|128uy; 128uy; 128uy; 255uy|]
let dummyNormal = createDummyTexture [|128uy; 128uy; 255uy; 255uy|]
let dummySpecular = createDummyTexture [|0uy; 0uy; 0uy; 0uy|]

let mouse = Input.Mouse(form)
let keyboard = Input.Keyboard(form)
let cameraController = Camera.CameraController(mouse, keyboard, Position = Vector3(-7.100705f, 47.303590f, 22.963710f), Yaw = -1.3f, Pitch = 0.15f)

let scene = List<Asset.Ref<Render.Mesh> * Matrix34 ref>()

let placeMeshRef path parent =
    try
        let mesh = loader.LoadAsync (".build/art/" + System.IO.Path.ChangeExtension(path, ".mesh"))
        scene.Add((mesh, parent))
    with e -> printfn "%s" e.Message

let placeMesh path parent =
    placeMeshRef path (ref parent)

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

let sceneName = "heaven"
// placeFile (sprintf "art/%s/%s.world" sceneName sceneName) Matrix34.Identity

let gizmoRef = ref (Matrix34.Translation(10.f-6.f, 10.f-2.f, 0.f) * Matrix34.Scaling(0.07f, 0.07f, 0.07f))

placeMeshRef "slave_driver/cc_slave_driver.mesh" gizmoRef
placeMesh "heaven/meshes/buildings/lab.mesh" Matrix34.Identity
placeMesh "heaven/meshes/buildings/lab_gears.mesh" Matrix34.Identity

let rng = System.Random()

let size = 10
for x = -size to size do
    for y = -size to size do
        placeMesh (sprintf "floor/floor%02d.mesh" (rng.Next(1, 15))) (Matrix34.Translation(float32 x * 2.f, float32 y * 2.f, 0.f))

let dbgSmoothness = Core.DbgVar(0.f, "lighting/smoothness")
let dbgRoughness = Core.DbgVar(0.5f, "lighting/roughness")

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
    member this.ViewInverse = Matrix34.InverseAffine(view)
    member this.Projection = projection
    member this.ProjectionInverse = Matrix44.Inverse(projection)
    member this.ViewProjection = projection * Matrix44(view)
    member this.ViewProjectionInverse = Matrix44.Inverse(this.ViewProjection)
    member this.EyePosition = Matrix34.InverseAffine(view).Column 3

[<Render.ShaderStruct>]
type Material(roughness: float32, smoothness: float32) =
    member this.Roughness = roughness
    member this.Smoothness = smoothness

let cbPool = Render.ConstantBufferPool(device.Device)

let renderScene (context: DeviceContext) (shaderContext: Render.ShaderContext) (camera: Camera) (shader: Render.Shader) =
    context.OutputMerger.DepthStencilState <- new DepthStencilState(device.Device, DepthStencilStateDescription(IsDepthEnabled = true, DepthWriteMask = DepthWriteMask.All, DepthComparison = Comparison.LessEqual))

    let fillMode = if dbgWireframe.Value then FillMode.Wireframe else FillMode.Solid
    context.Rasterizer.State <- new RasterizerState(device.Device, RasterizerStateDescription(CullMode = CullMode.Back, FillMode = fillMode, IsFrontCounterClockwise = true))

    context.InputAssembler.InputLayout <- layout
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList

    shaderContext.Shader <- shader

    shaderContext?camera <- camera
    shaderContext?defaultSampler <- new SamplerState(device.Device, SamplerStateDescription(AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap, Filter = dbgTexfilter.Value, MaximumAnisotropy = 16, MaximumLod = infinityf))

    let sceneCopy = lock scene (fun () -> scene.ToArray())

    for mesh, instances in sceneCopy |> Seq.choose (fun (mesh, transform) -> if mesh.IsReady then Some (mesh.Value, transform) else None) |> Seq.groupByRef (fun (mesh, transform) -> mesh) do
        if dbgNulldraw.Value then () else

        shaderContext?transforms <- instances |> Array.map (fun (mesh, transform) -> !transform)

        for fragment in mesh.fragments do
            let texture (tex: Asset.Ref<Render.Texture> option) dummy = if tex.IsSome && tex.Value.IsReady then tex.Value.Value.View else dummy

            let material = fragment.material

            shaderContext?albedoMap <- texture material.albedoMap dummyAlbedo
            shaderContext?normalMap <- texture material.normalMap dummyNormal
            shaderContext?specularMap <- texture material.specularMap dummySpecular

            shaderContext?meshCompressionInfo <- fragment.compressionInfo
            shaderContext?material <- Material(dbgRoughness.Value, dbgSmoothness.Value)

            shaderContext?mesh <- fragment.skin.ComputeBoneTransforms mesh.skeleton

            context.InputAssembler.SetVertexBuffers(0, VertexBufferBinding(mesh.vertices.Resource, vertexSize, fragment.vertexOffset))
            context.InputAssembler.SetIndexBuffer(mesh.indices.Resource, fragment.indexFormat, fragment.indexOffset)
            context.DrawIndexedInstanced(fragment.indexCount, instances.Length, 0, 0, 0)

let renderPass (context: DeviceContext) (shaderContext: Render.ShaderContext) (camera: Camera) (depthBuffer: Render.RenderTarget) (colorBuffers: Render.RenderTarget array) (viewport: Viewport) (shader: Render.Shader) =
    context.Rasterizer.SetViewports(viewport)
    context.OutputMerger.SetTargets(depthBuffer.DepthView, colorBuffers |> Array.map (fun rt -> rt.ColorView))

    PixHelper.BeginEvent(Color.Black, "scene render") |> ignore
    renderScene context shaderContext camera shader
    PixHelper.EndEvent() |> ignore

let renderFullScreenTri (context: DeviceContext) (shaderContext: Render.ShaderContext) (shader: Render.Shader) =
    context.InputAssembler.InputLayout <- null
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList
    context.OutputMerger.DepthStencilState <- new DepthStencilState(device.Device, DepthStencilStateDescription(IsDepthEnabled = false))
    context.Rasterizer.State <- new RasterizerState(device.Device, RasterizerStateDescription(CullMode = CullMode.None, FillMode = FillMode.Solid))

    shaderContext.Shader <- shader

    context.Draw(3, 0)

form.KeyUp.Add(fun args ->
    if args.Alt && args.KeyCode = System.Windows.Forms.Keys.Oemcomma then
        let w = WinUI.PropertyGrid.create (Core.DbgVars.getVariables() |> Array.map (fun (name, v) -> name, box v))
        w.Width <- 400.0
        w.KeyUp.Add (fun args -> if args.Key = System.Windows.Input.Key.Escape then w.Close())
        w.Show())

let dbgSpotOffset = Core.DbgVar(30.f, "spot/offset")
let dbgSpotHeight = Core.DbgVar(30.f, "spot/height")
let dbgSpotInnercone = Core.DbgVar(19.f, "spot/inner cone")
let dbgSpotOutercone = Core.DbgVar(20.f, "spot/outer cone")
let dbgSpotRadius = Core.DbgVar(40.f, "spot/radius")
let dbgSpotIntensity = Core.DbgVar(8.f, "spot/intensity")
let dbgSpotSeed = Core.DbgVar(1, "spot/seed")
let dbgSpotCount = Core.DbgVar(3, "spot/count")

type DebugTarget =
    | None = 0
    | Depth = 1

let dbgDebugTarget = Core.DbgVar(DebugTarget.None, "render/debug target")

type Timer() =
    let timer = Diagnostics.Stopwatch()
    let times = Array.create 32 0.f
    let mutable index = 0

    member this.Start() =
        index <- (index + 1) % times.Length
        timer.Restart()

    member this.Stop() =
        timer.Stop()
        times.[index] <- float32 timer.Elapsed.TotalSeconds

    member this.Current = times.[index]

    member this.Average = Array.average times
    member this.Min = Array.min times
    member this.Max = Array.max times

    override this.ToString() = sprintf "%.2f ms [%.2f..%.2f]" (1000.f * this.Average) (1000.f * this.Min) (1000.f * this.Max)

// mesh reloader, bwuhahaha
let dbgReloadMeshes = Core.DbgVar(false, "reload meshes")

Async.Start <| async {
    let files = BuildSystem.Node.Glob ".build/**.mesh"

    for i in Seq.initInfinite id do
        while not dbgReloadMeshes.Value do
            do! Async.Sleep 100
        try
            let mesh = loader.Load files.[i % files.Length].Path
            lock scene (fun () -> scene.RemoveAt(scene.Count - 1); scene.Add((mesh, ref (Matrix34.Translation(10.f, 10.f, 0.f)))))
        with e ->
            printfn "%s" e.Message
}

let frameTimer = Timer()
let bodyTimer = Timer()
let presentTimer = Timer()

frameTimer.Start()

let getPerspectiveCorrectLerpFactor (A: float32) (B: float32) (t: float32) =
    // lerp(A, B, u) = lerp_persp(A, B, t) = 1 / lerp(1 / A, 1 / B, t)
    // A + (B - A) * u = 1 / (1 / A + (1 / B - 1 / A) * t)
    // u = (A * t) / (B + (A - B) * t)
    (A * t) / (B + (A - B) * t)

let getMoveOffset (point: Vector3) (axis: Vector3) (viewProjection: Matrix44) (dx: int) (dy: int) (width: int) (height: int) =
    let fromClip = Matrix44.Transform(viewProjection, Vector4(point, 1.f))
    let toClip = Matrix44.Transform(viewProjection, Vector4(point + axis, 1.f))

    let dirClip = toClip.xy / toClip.w - fromClip.xy / fromClip.w
    let dxdyClip = Vector2(float32 dx / float32 (width - 1) * 2.f, float32 dy / float32 (height - 1) * -2.f)

    let lerpClip = Vector2.Dot(dirClip, dxdyClip) / dirClip.LengthSquared
    let lerpWorld = getPerspectiveCorrectLerpFactor fromClip.w toClip.w lerpClip

    lerpWorld

let cursorPos = ref (0, 0)

let getLightGrid =
    let cache = ref (None: LightGrid option)
    let matches sizePixels tileCount tileSize =
        tileCount = (sizePixels + tileSize - 1) / tileSize

    fun width height ->
        match !cache with
        | Some grid when matches width grid.Width LightGrid.CellSize && matches height grid.Height LightGrid.CellSize -> grid
        | _ ->
            let grid = LightGrid(device, width, height, 128)
            cache := Some grid
            grid

RenderLoop.Run(form, fun () ->
    frameTimer.Stop()
    let dt = frameTimer.Current
    frameTimer.Start()

    form.Text <- sprintf "frame: %O; body: %O; present: %O" frameTimer bodyTimer presentTimer

    bodyTimer.Start()

    mouse.Update()
    keyboard.Update()
    cameraController.Update(dt)

    let context = device.Device.ImmediateContext
    use shaderContext = new Render.ShaderContext(cbPool, context)

    context.ClearState()

    let lightGrid = getLightGrid form.ClientSize.Width form.ClientSize.Height

    // main camera
    let camera = Camera(cameraController.ViewMatrix, Math.Camera.projectionPerspective (dbgFov.Value / 180.f * float32 Math.PI) (float32 form.ClientSize.Width / float32 form.ClientSize.Height) 0.1f 1000.f)

    // move gizmo test
    let dx = mouse.CursorX - fst !cursorPos
    let dy = mouse.CursorY - snd !cursorPos
    cursorPos := (mouse.CursorX, mouse.CursorY)

    match
        match [|Input.Key.X; Input.Key.Y; Input.Key.Z|] |> Array.map keyboard.KeyDown with
        | [|true; _; _|] -> Some Vector3.UnitX
        | [|_; true; _|] -> Some Vector3.UnitY
        | [|_; _; true|] -> Some Vector3.UnitZ
        | _ -> None
        with
    | Some axis ->
        let offset = getMoveOffset ((!gizmoRef).Column 3) axis camera.ViewProjection dx dy form.ClientSize.Width form.ClientSize.Height
        gizmoRef := Matrix34.Translation(axis * offset) * !gizmoRef
    | None -> ()

    let viewport = Viewport(0.f, 0.f, float32 form.ClientSize.Width, float32 form.ClientSize.Height)

    // fill depth buffer
    use depthBuffer = rtpool.Acquire("gbuffer/depth", form.ClientSize.Width, form.ClientSize.Height, Format.D24_UNorm_S8_UInt)

    context.ClearDepthStencilView(depthBuffer.DepthView, DepthStencilClearFlags.Depth, 1.f, 0uy)

    renderPass context shaderContext camera depthBuffer [||] viewport depthFill.Value

    // create lights
    let deg2rad = float32 System.Math.PI / 180.f

    let colors = [|
        Math.Color4(1.f, 0.f, 0.f)
        Math.Color4(0.f, 1.f, 0.f)
        Math.Color4(0.f, 0.f, 1.f)
        Math.Color4(1.f, 0.f, 1.f)
        Math.Color4(1.f, 1.f, 0.f)
        Math.Color4(0.f, 1.f, 1.f)
        Math.Color4(1.f, 1.f, 1.f) |]

    let rng = System.Random(dbgSpotSeed.Value)

    let lights =
        Array.append [|DirectionalLight { direction = Vector3.Normalize (Vector3(-1.f, -1.f, -1.f)); color = Color4(1.f, 1.f, 0.f); intensity = 1.f }|] $
        Array.init dbgSpotCount.Value (fun i ->
            // generate procedural spot light
            let angle = deg2rad * 360.f * (float32 i / float32 dbgSpotCount.Value)
            let position = Vector3(dbgSpotOffset.Value * sin angle, dbgSpotOffset.Value * cos angle, dbgSpotHeight.Value)

            if i % 2 = 0 then
                PointLight {
                    position = position
                    radius = dbgSpotRadius.Value
                    color = colors.[i % colors.Length]
                    intensity = dbgSpotIntensity.Value }
            else
                SpotLight {
                    position = position
                    direction = Vector3.Normalize(-position)
                    radius = dbgSpotRadius.Value
                    innerAngle = deg2rad * dbgSpotInnercone.Value
                    outerAngle = deg2rad * dbgSpotOutercone.Value
                    color = colors.[i % colors.Length]
                    intensity = dbgSpotIntensity.Value })

    let shadowAtlasWidth = 4096
    let shadowAtlasHeight = 4096
    let lightCullData, lightData = LightDataBuilder.build lights shadowAtlasWidth shadowAtlasHeight

    context.OutputMerger.SetTargets(null, [||])

    // fill lightgrid
    shaderContext?lightGridBufferUA <- lightGrid.UnorderedView
    shaderContext?lightGrid <- lightGrid
    shaderContext?lightCullData <- lightCullData
    shaderContext?lightCount <- box lights.Length
    shaderContext?depthBuffer <- depthBuffer.View
    shaderContext?camera <- camera

    shaderContext.Program <- lightGridFill.Value
    context.Dispatch(lightGrid.Width, lightGrid.Height, 1)

    shaderContext?lightGridBufferUA <- (null: UnorderedAccessView)
    shaderContext?lightGridBuffer <- lightGrid.View

    shaderContext?lightData <- lightData

    // render shadows
    use shadowBuffer = rtpool.Acquire("lighting/shadow buffer", shadowAtlasWidth, shadowAtlasHeight, Format.D24_UNorm_S8_UInt)

    context.ClearDepthStencilView(shadowBuffer.DepthView, DepthStencilClearFlags.Depth, 1.f, 0uy)

    for light in lightData do
        let shadow = light.ShadowData

        if shadow.AtlasScale.LengthSquared > 0.f then
            // render shadow map
            let camera = Camera(Matrix34.Identity, shadow.Transform)
            let viewport =
                Viewport(
                    shadow.AtlasOffset.x * float32 shadowAtlasWidth, shadow.AtlasOffset.y * float32 shadowAtlasHeight, 
                    shadow.AtlasScale.x * float32 shadowAtlasWidth, shadow.AtlasScale.y * float32 shadowAtlasHeight)

            renderPass context shaderContext camera shadowBuffer [||] viewport depthFill.Value

    shaderContext?shadowMap <- shadowBuffer.View
    shaderContext?shadowSampler <- new SamplerState(device.Device, SamplerStateDescription(AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, Filter = Filter.ComparisonMinMagMipLinear, ComparisonFunction = Comparison.Less))

    // fill color buffer
    use colorBuffer = rtpool.Acquire("scene/hdr", form.ClientSize.Width, form.ClientSize.Height, Format.R16G16B16A16_Float)

    context.ClearRenderTargetView(colorBuffer.ColorView, SharpDX.Color4 0xff808080)

    renderPass context shaderContext camera depthBuffer [|colorBuffer|] viewport gbufferFill.Value

    // tonemap
    use ldrBuffer = rtpool.Acquire("scene/ldr", form.ClientSize.Width, form.ClientSize.Height, Format.R8G8B8A8_UNorm)

    context.OutputMerger.SetTargets(ldrBuffer.ColorView)

    shaderContext?defaultSampler <- new SamplerState(device.Device, SamplerStateDescription(AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, Filter = Filter.MinMagMipLinear))
    shaderContext?colorMap <- colorBuffer.View

    renderFullScreenTri context shaderContext postfxTonemap.Value

    // fxaa blit
    context.OutputMerger.SetTargets(device.BackBuffer.ColorView)

    shaderContext?defaultSampler <- new SamplerState(device.Device, SamplerStateDescription(AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, Filter = Filter.MinMagMipLinear))
    shaderContext?colorMap <- ldrBuffer.View

    renderFullScreenTri context shaderContext postfxFxaa.Value

    // blend lightgrid debug output over
    shaderContext?lightGrid <- lightGrid

    let mutable blendon = BlendStateDescription()
    Array.fill blendon.RenderTarget 0 8 (RenderTargetBlendDescription(IsBlendEnabled = true, SourceBlend = BlendOption.One, DestinationBlend = BlendOption.One, BlendOperation = BlendOperation.Add, SourceAlphaBlend = BlendOption.One, DestinationAlphaBlend = BlendOption.Zero, AlphaBlendOperation = BlendOperation.Add, RenderTargetWriteMask = ColorWriteMaskFlags.All))

    context.OutputMerger.BlendState <- new BlendState(device.Device, blendon)

    renderFullScreenTri context shaderContext lightGridDebug.Value

    context.OutputMerger.BlendState <- null

    match 
        match dbgDebugTarget.Value with
        | DebugTarget.None -> None
        | DebugTarget.Depth -> Some depthBuffer
        | t -> failwithf "Unknown target value %O" t
        with
    | Some rt ->
        shaderContext?colorMap <- rt.View
        shaderContext?blitUnpackDepth <- box (dbgDebugTarget.Value = DebugTarget.Depth)

        renderFullScreenTri context shaderContext postfxBlit.Value
    | None -> ()

    bodyTimer.Stop()

    presentTimer.Start()

    device.SwapChain.Present(dbgPresentInterval.Value, PresentFlags.None) |> ignore

    presentTimer.Stop()

    firsttime.Force() |> ignore)

device.Device.ImmediateContext.ClearState()
device.Device.ImmediateContext.Flush()
