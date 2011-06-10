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

[<STAThread>]
do()

// initial setup
System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo.InvariantCulture
System.Environment.CurrentDirectory <- System.AppDomain.CurrentDomain.BaseDirectory + "/../.."
System.Console.WindowWidth <- max System.Console.WindowWidth 140

// run tests
Core.Test.run ()

// build assets
assets.context.Run()

// start asset watcher
assets.watcher.Force() |> ignore

let dbg_wireframe = Core.DbgVar(false, "render/wireframe")
let dbg_present_interval = Core.DbgVar(0, "vsync interval")
let dbg_name = Core.DbgVar("foo", "name")
let dbg_texfilter = Core.DbgVar(Filter.Anisotropic, "render/texture/filter")
let dbg_fov = Core.DbgVar(45.f, "camera/fov")

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

// setup asset loaders
AssetDB.addType "dds" (Render.TextureLoader.load device)
AssetDB.addType "mesh" (fun path -> (Core.Serialization.Load.fromFileEx path device) :?> Render.Mesh)
AssetDB.addType "hlsl" (fun path -> Effect(device, path))

let factory = swapChain.GetParent<Factory>()
factory.SetWindowAssociation(form.Handle, WindowAssociationFlags.IgnoreAll) |> ignore

let backBuffer = Texture2D.FromSwapChain<Texture2D>(swapChain, 0)
let backBufferView = new RenderTargetView(device, backBuffer)

let depthBuffer = new Texture2D(device, Texture2DDescription(Width = form.ClientSize.Width, Height = form.ClientSize.Height, Format = Format.D24_UNorm_S8_UInt, ArraySize = 1, MipLevels = 1, SampleDescription = SampleDescription(1, 0), BindFlags = BindFlags.DepthStencil))
let depthBufferView = new DepthStencilView(device, depthBuffer)

let basic_textured = Asset<Effect>.Load "src/shaders/basic_textured.hlsl"

let vertex_size = (Render.VertexLayouts.get Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed).size
let layout = new InputLayout(device, basic_textured.Data.VertexSignature, (Render.VertexLayouts.get Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed).elements)

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

let camera_controller = Camera.CameraController()

let scene = List<Asset<Render.Mesh> * Matrix34>()

module MJSON =
    module private Lexer =
        type Lexeme =
        | Identifier of string
        | String of string
        | Number of float
        | Token of char
        | End

        let readWhile (s: TextReader) pred =
            let sb = StringBuilder()
            while s.Peek() <> -1 && s.Peek() |> char |> pred do
                sb.Append(s.Read() |> char) |> ignore
            sb.ToString()

        let rec next (s: TextReader) =
            match s.Read() with
            | -1 -> End
            | b ->
                match char b with
                | '\t' | '\r' | '\n' | ' ' -> next s
                | '/' ->
                    match s.Read() |> char with
                    | '/' ->
                        while s.Read() <> int '\n' do ()
                        next s
                    | c -> failwith "Unexpected character '%c' (expected '/')" c
                | '=' | '{' | '}' | '[' | ']' | ',' as c -> Token c
                | '"' ->
                    let sb = StringBuilder()
                    let rec loop () =
                        match s.Read() with
                        | -1 -> failwith "Unexpected end of input (unterminated string)"
                        | c when char c = '"' -> ()
                        | c ->
                            sb.Append((if char c = '\\' then s.Read() else c) |> char) |> ignore
                            loop ()
                    loop ()
                    String (sb.ToString())
                | c when (c = '-' || c = '.' || Char.IsDigit(c)) ->
                    let value = c.ToString() + readWhile s (fun c -> c = '-' || c = '.' || c = 'e' || c = 'E' || c = '+' || c = '-' || Char.IsDigit(c))
                    Number (float value)
                | c when (c = '_' || Char.IsLetterOrDigit(c)) ->
                    let value = c.ToString() + readWhile s (fun c -> c = '_' || Char.IsLetterOrDigit(c))
                    Identifier value
                | c -> failwithf "Unknown character '%c'" c

    type Node =
    | Number of float
    | String of string
    | Array of Node array
    | Object of (string * Node) array

    let rec private parseFields s term =
        let rec loop acc =
            match Lexer.next s with
            | Lexer.Identifier id ->
                let value =
                    match Lexer.next s with
                    | Lexer.Token '=' ->
                        parseNode s (Lexer.next s)
                    | l -> parseNode s l
                loop ((id, value) :: acc)
            | t when t = term -> acc
            | l -> failwith "Expected '%A' or identifier, got '%A'" term s
        Object (loop [] |> List.toArray |> Array.rev)

    and private parseNode s l =
        match l with
        | Lexer.Number v -> Node.Number v
        | Lexer.String s -> Node.String s
        | Lexer.Token '[' ->
            let rec loop acc =
                match Lexer.next s with
                | Lexer.Token ']' -> acc
                | l ->
                    let n = parseNode s l
                    match Lexer.next s with
                    | Lexer.Token ',' -> loop (n :: acc)
                    | Lexer.Token ']' -> n :: acc
                    | l -> failwithf "Expected ',' or ']', got '%A'" l
            Array (loop [] |> List.toArray |> Array.rev)
        | Lexer.Token '{' ->
            parseFields s (Lexer.Token '}')
        | l -> failwithf "Unexpected token '%A'" l

    let parse (s: TextReader) =
        parseFields s Lexer.End

    let parseFile path =
        use s = File.OpenText(path)
        parse s

let placeMesh path parent =
    try
        let mesh = Asset.LoadAsync (".build/art/" + System.IO.Path.ChangeExtension(path, ".mesh"))
        let fixup = Matrix34(Vector4.UnitX, Vector4.UnitZ, Vector4.UnitY)
        scene.Add((mesh, parent))
    with e -> printfn "%s" e.Message

let placeFile path parent =
    let nodes =
        match MJSON.parseFile path with
        | MJSON.Object list -> list |> dict
        | _ -> failwith "Expected object"

    let parseTransform node =
        match node with
        | MJSON.Array elements ->
            let v = elements |> Array.map (function | MJSON.Number n -> float32 n | _ -> failwith "Incorrect transform element")
            assert (v.Length = 12)
            Matrix34(v.[0], v.[1], v.[2], v.[3], v.[4], v.[5], v.[6], v.[7], v.[8], v.[9], v.[10], v.[11])
        | _ -> failwith "Incorrect transform value"
        
    let rec placeNode node parent =
        match node with
        | MJSON.Object list ->
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
                        | MJSON.Object ([|"path", MJSON.String path|]) -> placeMesh path parent
                        | _ -> failwith "Incorrect mesh node"
                        parent
                    | "reference" ->
                        match value with
                        | MJSON.Object ([|"name", MJSON.String name|]) -> placeNode nodes.[name] parent
                        | _ -> failwith "Incorrect reference node"
                        parent
                    | _ -> parent) parent
            |> ignore
        | _ -> failwith "Expected object"

    let root = Path.GetFileNameWithoutExtension(path)

    placeNode nodes.[root + "_" + root] parent

let scene_name = "heaven"
placeFile (sprintf "art/%s/%s.world" scene_name scene_name) Matrix34.Identity

let draw (context: DeviceContext) =
    let projection = Math.Camera.projectionPerspective (dbg_fov.Value / 180.f * float32 Math.PI) (float32 form.ClientSize.Width / float32 form.ClientSize.Height) 0.1f 1000.f
    let view = camera_controller.ViewMatrix
    let view_projection = projection * Matrix44(view)

    context.Rasterizer.SetViewports(new Viewport(0.f, 0.f, float32 form.ClientSize.Width, float32 form.ClientSize.Height))
    context.OutputMerger.SetTargets(depthBufferView, backBufferView)
    context.OutputMerger.DepthStencilState <- DepthStencilState.FromDescription(device, DepthStencilStateDescription(IsDepthEnabled = true, DepthWriteMask = DepthWriteMask.All, DepthComparison = Comparison.Less))

    let fill_mode = if dbg_wireframe.Value then FillMode.Wireframe else FillMode.Solid
    context.Rasterizer.State <- RasterizerState.FromDescription(device, RasterizerStateDescription(CullMode = CullMode.Back, FillMode = fill_mode, IsFrontCounterclockwise = true))

    context.InputAssembler.InputLayout <- layout
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList

    context.VertexShader.Set(basic_textured.Data.VertexShader)
    context.PixelShader.Set(basic_textured.Data.PixelShader)

    context.VertexShader.SetConstantBuffer(constantBuffer0, 0)
    context.VertexShader.SetConstantBuffer(constantBuffer1, 1)
    context.PixelShader.SetSampler(SamplerState.FromDescription(device, SamplerDescription(AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap, Filter = dbg_texfilter.Value, MaximumAnisotropy = 16, MaximumLod = infinityf)), 0)

    for mesh, instances in scene |> Seq.choose (fun (mesh, transform) -> if mesh.IsReady then Some (mesh.Data, transform) else None) |> Seq.groupBy (fun (mesh, transform) -> mesh) do
        let transforms = instances |> Seq.toArray |> Array.map (fun (mesh, transform) -> transform)

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
