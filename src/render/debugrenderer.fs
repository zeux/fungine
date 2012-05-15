namespace Render

open System.Collections.Generic

open SharpDX.Direct3D11

[<RequireQualifiedAccess>]
type DebugPrimitive =
    | Box of Matrix34 * Math.AABB * Color4

type DebugRenderer(device: Device, shader: Shader) =
    let format = VertexLayouts.get VertexFormat.Pos_Color
    let vertices = new Buffer(device, format.size * 1024, ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0)
    let layout = new InputLayout(device, shader.VertexSignature.Resource, format.elements)

    let primitives = List<DebugPrimitive>()

    let boxEdges =
        [| 0,1; 1,3; 3,2; 2,0;
           4,5; 5,7; 7,6; 6,4;
           0,4; 1,5; 2,6; 3,7 |]

    // add debug primitive
    member this.Add(prim) =
        primitives.Add(prim)

    // flush primitives to render context
    member this.Flush(context: DeviceContext, shaderContext: ShaderContext) =
        shaderContext.Shader <- shader

        context.InputAssembler.InputLayout <- layout
        context.InputAssembler.PrimitiveTopology <- PrimitiveTopology.LineList
        context.InputAssembler.SetVertexBuffers(0, VertexBufferBinding(vertices, format.size, 0))

        let data = ref $ context.MapSubresource(vertices, MapMode.WriteDiscard, MapFlags.None)

        let line (v0: Vector3) (v1: Vector3) (color: Color4) =
            if (snd !data).RemainingLength = 0L then
                context.UnmapSubresource(vertices, 0)
                context.Draw(int (snd !data).Position / format.size, 0)
                data := context.MapSubresource(vertices, MapMode.WriteDiscard, MapFlags.None)

            (snd !data).Write(v0)
            (snd !data).Write(color)
            (snd !data).Write(v1)
            (snd !data).Write(color)

        for p in primitives do
            match p with
            | DebugPrimitive.Box (transform, aabb, color) ->
                let inline point i =
                    Matrix34.TransformPosition(transform,
                        Vector3(
                            (if i &&& 1 = 0 then aabb.Min.x else aabb.Max.x),
                            (if i &&& 2 = 0 then aabb.Min.y else aabb.Max.y),
                            (if i &&& 4 = 0 then aabb.Min.z else aabb.Max.z)))

                for (s, e) in boxEdges do
                    line (point s) (point e) color

        context.UnmapSubresource(vertices, 0)
        context.Draw(int (snd !data).Position / format.size, 0)

        primitives.Clear()