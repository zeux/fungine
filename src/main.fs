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
        let bytecode_vs = ShaderBytecode.CompileFromFile(path, "vs_main", "vs_5_0", ShaderFlags.PackMatrixRowMajor ||| ShaderFlags.WarningsAreErrors, EffectFlags.None)
        let bytecode_ps = ShaderBytecode.CompileFromFile(path, "ps_main", "ps_5_0", ShaderFlags.PackMatrixRowMajor ||| ShaderFlags.WarningsAreErrors, EffectFlags.None)

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

let (_, device, swapChain) = Device.CreateWithSwapChain(DriverType.Hardware, DeviceCreationFlags.None, desc)

let factory = swapChain.GetParent<Factory>()
factory.SetWindowAssociation(form.Handle, WindowAssociationFlags.IgnoreAll) |> ignore

let backBuffer = Texture2D.FromSwapChain<Texture2D>(swapChain, 0)
let backBufferView = new RenderTargetView(device, backBuffer)

let depthBuffer = new Texture2D(device, Texture2DDescription(Width = form.ClientSize.Width, Height = form.ClientSize.Height, Format = Format.D24_UNorm_S8_UInt, ArraySize = 1, MipLevels = 1, SampleDescription = SampleDescription(1, 0), BindFlags = BindFlags.DepthStencil))
let depthBufferView = new DepthStencilView(device, depthBuffer)

let basic_textured = Effect(device, "src/shaders/basic_textured.hlsl")

let vertex_size = 28

let layout = new InputLayout(device, basic_textured.VertexSignature,
                [|
                InputElement("POSITION", 0, Format.R16G16B16A16_UNorm, 0, 0)
                InputElement("NORMAL", 0, Format.R8G8B8A8_SNorm, 8, 0)
                InputElement("TANGENT", 0, Format.R8G8B8A8_SNorm, 12, 0)
                InputElement("TEXCOORD", 0, Format.R16G16_UNorm, 16, 0)
                InputElement("BONEINDICES", 0, Format.R8G8B8A8_UInt, 20, 0)
                InputElement("BONEWEIGHTS", 0, Format.R8G8B8A8_UNorm, 24, 0)
                |])

type CompressionInfo =
    { position_offset: Vector3
      position_scale: Vector3
      texcoord_offset: Vector2
      texcoord_scale: Vector2 }

let createRenderVertexBuffer (vertices: Build.Geometry.FatVertex array) =
    let stream = new DataStream((int64 vertex_size) * vertices.LongLength, true, true)

    // get position and texcoord bounds for compression
    let positions = vertices |> Array.map (fun v -> v.position)
    let position_min = positions |> Array.reduce (fun a b -> Vector3.Minimize(a, b))
    let position_max = positions |> Array.reduce (fun a b -> Vector3.Maximize(a, b))
    let position_scale = position_max - position_min

    let texcoords = vertices |> Array.map (fun v -> if v.texcoord <> null then v.texcoord.[0] else Vector2())
    let texcoord_min = texcoords |> Array.reduce (fun a b -> Vector2.Minimize(a, b))
    let texcoord_max = texcoords |> Array.reduce (fun a b -> Vector2.Maximize(a, b))
    let texcoord_scale = texcoord_max - texcoord_min

    let compression_info = { new CompressionInfo with position_offset = position_min and position_scale = position_scale and texcoord_offset = texcoord_min and texcoord_scale = texcoord_scale }

    for v in vertices do
        stream.Write(uint16 (Math.Pack.packFloatUNorm ((v.position.X - position_min.X) / position_scale.X) 16))
        stream.Write(uint16 (Math.Pack.packFloatUNorm ((v.position.Y - position_min.Y) / position_scale.Y) 16))
        stream.Write(uint16 (Math.Pack.packFloatUNorm ((v.position.Z - position_min.Z) / position_scale.Z) 16))
        stream.Write(uint16 0)
        
        stream.Write(Math.Pack.packDirectionR8G8B8(v.normal))
        stream.Write(Math.Pack.packDirectionR8G8B8(v.tangent) ||| (if Vector3.Dot(Vector3.Cross(v.normal, v.tangent), v.bitangent) > 0.f then 0x7F000000u else 0x80000000u))

        let uv0 = if v.texcoord <> null then v.texcoord.[0] else Vector2()

        stream.Write(uint16 (Math.Pack.packFloatUNorm ((uv0.X - texcoord_min.X) / texcoord_scale.X) 16))
        stream.Write(uint16 (Math.Pack.packFloatUNorm ((uv0.Y - texcoord_min.Y) / texcoord_scale.Y) 16))

        let bone_indices : byte array = Array.zeroCreate 4
        let bone_weights : byte array = Array.zeroCreate 4

        // hack for non-skinned objects
        bone_weights.[0] <- 255uy

        if v.bones <> null then
            v.bones |> Array.iteri (fun index bone ->
                bone_indices.[index] <- byte bone.index
                bone_weights.[index] <- byte (Math.Pack.packFloatUNorm bone.weight 8))

        stream.WriteRange(bone_indices)
        stream.WriteRange(bone_weights)

    stream.Position <- 0L

    compression_info, new Buffer(device, stream, BufferDescription(int stream.Length, ResourceUsage.Default, BindFlags.VertexBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, vertex_size))

let createRenderIndexBuffer (indices: int array) =
    let stream = new DataStream(4L * indices.LongLength, true, true)

    stream.WriteRange(indices)
    stream.Position <- 0L

    new Buffer(device, stream, BufferDescription(int stream.Length, ResourceUsage.Default, BindFlags.IndexBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 4))

let renderMeshes =
    meshes |> Array.map (fun (mesh, skeleton, transform) ->
        mesh, skeleton, transform, createRenderVertexBuffer mesh.vertices, createRenderIndexBuffer mesh.indices)

let projection = Matrix.PerspectiveFovLH(45.f, float32 form.ClientSize.Width / float32 form.ClientSize.Height, 1.f, 1000.f)
let view = Matrix.LookAtLH(Vector3(0.f, 20.f, 35.f), Vector3(0.f, 15.f, 0.f), Vector3(0.f, 1.f, 0.f)) * Matrix.Scaling(-1.f, 1.f, 1.f)
let view_projection = view * projection

let constantBuffer0 = new Buffer(device, null, BufferDescription(16448, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 16448))
let constantBuffer1 = new Buffer(device, null, BufferDescription(65536, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 65536))

let albedo_map = Texture2D.FromFile(device, ".build/art/slave_driver/ch_barb_slavedriver_01.dds")
let normal_map = Texture2D.FromFile(device, ".build/art/slave_driver/ch_barb_slavedriver_01_nm.dds")
let specular_map = Texture2D.FromFile(device, ".build/art/slave_driver/ch_barb_slavedriver_01_spec.dds")

let contextHolder = new ObjectPool(fun _ -> new DeviceContext(device))

let draw (context: DeviceContext) =
    context.Rasterizer.SetViewports(new Viewport(0.f, 0.f, float32 form.ClientSize.Width, float32 form.ClientSize.Height))
    context.OutputMerger.SetTargets(depthBufferView, backBufferView)
    context.OutputMerger.DepthStencilState <- DepthStencilState.FromDescription(device, DepthStencilStateDescription(IsDepthEnabled = true, DepthWriteMask = DepthWriteMask.All, DepthComparison = Comparison.Less))
    context.Rasterizer.State <- RasterizerState.FromDescription(device, RasterizerStateDescription(CullMode = CullMode.Back, FillMode = FillMode.Solid, IsFrontCounterclockwise = true))

    context.InputAssembler.InputLayout <- layout
    context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.TriangleList

    context.VertexShader.Set(basic_textured.VertexShader)
    context.PixelShader.Set(basic_textured.PixelShader)

    context.VertexShader.SetConstantBuffer(constantBuffer0, 0)
    context.VertexShader.SetConstantBuffer(constantBuffer1, 1)
    context.PixelShader.SetSampler(SamplerState.FromDescription(device, SamplerDescription(AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap, Filter = Filter.Anisotropic, MaximumAnisotropy = 16)), 0)

    context.PixelShader.SetShaderResource(new ShaderResourceView(device, albedo_map :> Resource), 0)
    context.PixelShader.SetShaderResource(new ShaderResourceView(device, normal_map :> Resource), 1)
    context.PixelShader.SetShaderResource(new ShaderResourceView(device, specular_map :> Resource), 2)

    let drawInstanced offsets =
        let box = context.MapSubresource(constantBuffer1, 0, constantBuffer1.Description.SizeInBytes, MapMode.WriteDiscard, MapFlags.None)
        box.Data.WriteRange(offsets)
        context.UnmapSubresource(constantBuffer1, 0)

        for mesh, skeleton, transform, (compression_info, vb), ib in renderMeshes do
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
            context.InputAssembler.SetIndexBuffer(ib, Format.R32_UInt, 0)
            context.DrawIndexedInstanced(mesh.indices.Length, offsets.Length, 0, 0, 0)

    if false then
        drawInstanced [| Matrix.Identity |]
    else
        let rng = System.Random(123456789)

        let offsets = [|0..1000|] |> Array.map (fun _ ->
            let x = -20.f + 40.f * float32 (rng.NextDouble())
            let y = -25.f + 40.f * float32 (rng.NextDouble())
            Matrix.Scaling(0.1f, 0.1f, 0.1f) * Matrix.Translation(x, 7.f, y))

        drawInstanced offsets

    context.FinishCommandList(false)

MessagePump.Run(form, fun () ->

    device.ImmediateContext.ClearRenderTargetView(backBufferView, Color4 Color.Black)
    device.ImmediateContext.ClearDepthStencilView(depthBufferView, DepthStencilClearFlags.Depth, 1.f, 0uy)

    let context = contextHolder.get()
    use cl = draw context
    contextHolder.put(context)

    device.ImmediateContext.ExecuteCommandList(cl, false)

    swapChain.Present(0, PresentFlags.None) |> ignore
)

device.ImmediateContext.ClearState()
device.ImmediateContext.Flush()
