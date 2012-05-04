namespace Render

open System
open System.Collections.Concurrent

open SharpDX.Data
open SharpDX.Direct3D11

// a slot in the pool contains constant buffers for a specific size
type ConstantBufferPoolSlot =
    { scratch: DataStream
      free: ConcurrentStack<Buffer> }

// constant buffer holder; dispose to release the buffer object into pool
[<Struct>]
type ConstantBuffer(slot: ConstantBufferPoolSlot, buffer: Buffer) =
    interface IDisposable with
        member this.Dispose() =
            if buffer <> null then
                slot.free.Push(buffer)

    // buffer object
    member this.Buffer = buffer

    // scratch memory (for UpdateSubresource)
    member this.Scratch = slot.scratch

// constant buffer pool
type ConstantBufferPool(device: Device) =
    let pool = Core.ConcurrentCache(fun size ->
        { new ConstantBufferPoolSlot with scratch = new DataStream(size, canRead = true, canWrite = true) and free = ConcurrentStack<_>() })

    // acquire a constant buffer of the specified size
    member this.Acquire size =
        // get matching slot
        let slot = pool.Get size

        let mutable cb = null

        // if there is no free buffer, create a new one; it will be returned to pool on Dispose
        if not (slot.free.TryPop(&cb)) then
            cb <- new Buffer(device, BufferDescription(size, ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0))

        new ConstantBuffer(slot, cb)