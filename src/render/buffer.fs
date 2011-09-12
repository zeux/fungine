namespace Render

open SlimDX.Direct3D11

// vertex/index buffer
type Buffer(bindFlags, contents: byte array) =
    // buffer object
    [<System.NonSerialized>]
    let mutable data = null

    // fixup callback
    member private this.Fixup device =
        use stream = new SlimDX.DataStream(contents, canRead = true, canWrite = false)
        data <- new SlimDX.Direct3D11.Buffer(device, stream, BufferDescription(SizeInBytes = contents.Length, BindFlags = bindFlags))

    // resource accessor
    member this.Resource = data