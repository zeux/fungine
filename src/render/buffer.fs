namespace Render

open SlimDX.Direct3D11

// vertex/index buffer
type Buffer(bind_flags, contents: byte array) =
    // buffer object
    [<System.NonSerialized>]
    let mutable data = null

    // lazy initializing resource accessor ($$ replace with post-serialization callback)
    member this.Resource =
        if data = null then
            use stream = new SlimDX.DataStream(contents, canRead = true, canWrite = false)
            data <- new SlimDX.Direct3D11.Buffer(Render.Device.get(), stream, BufferDescription(SizeInBytes = contents.Length, BindFlags = bind_flags))
        data