namespace Render

open SharpDX.Data
open SharpDX.Direct3D11

// vertex/index buffer
type GeometryBuffer(bindFlags, contents: byte array) =
    // buffer object
    [<System.NonSerialized>]
    let mutable data = null

    // fixup callback
    member private this.Fixup ctx =
        let device = Core.Serialization.Fixup.Get<Device>(ctx)
        use stream = DataStream.Create(contents, canRead = true, canWrite = false, makeCopy = false)
        data <- new Buffer(device, stream, BufferDescription(SizeInBytes = contents.Length, BindFlags = bindFlags))

    // resource accessor
    member this.Resource = data

// vertex buffer
type VertexBuffer(contents) =
    inherit GeometryBuffer(BindFlags.VertexBuffer, contents)

// index buffer
type IndexBuffer(contents) =
    inherit GeometryBuffer(BindFlags.IndexBuffer, contents)