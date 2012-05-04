namespace Render

open System
open System.Collections.Generic

open SharpDX.Data
open SharpDX.Direct3D11

#nowarn "1173" // F# 3.0 compiler regression on ?<- operator as class member

module private ShaderUtil =
    // dynamically-indexed arrays of arbitrary sizes are declared as Type name[2] in the shader, because name[1]
    // results in fxc converting dynamic indexing to static; so let's make sure we're uploading at least 2 elements
    // to keep DXDebug happy
    let minArraySize = 2

    // upload a single object or an object array into a constant buffer
    let uploadConstantData (cbPool: ConstantBufferPool) (context: DeviceContext) (data: obj) =
        let elementSize, upload = Render.ShaderStruct.getUploadDelegate (data.GetType())
        let size = elementSize * (match data with :? Array as a -> max minArraySize a.Length | _ -> 1)
        let cb = cbPool.Acquire size
        let scratch = cb.Scratch
        upload.Invoke(data, scratch.DataPointer, size)
        context.UpdateSubresource(DataBox(scratch.DataPointer), cb.Buffer, 0)
        cb

    // update context stage with parameter binding
    let updateBinding (p: Render.ShaderParameter) (value: obj) (stage: CommonShaderStage) =
        match p.Binding with
        | Render.ShaderParameterBinding.None -> ()
        | Render.ShaderParameterBinding.ConstantBuffer -> stage.SetConstantBuffer(p.Register, unbox value)
        | Render.ShaderParameterBinding.ShaderResource -> stage.SetShaderResource(p.Register, unbox value)
        | Render.ShaderParameterBinding.Sampler -> stage.SetSampler(p.Register, unbox value)
        | x -> failwithf "Unexpected binding value %A" x

    // update context stage with parameter binding with UA support
    let updateBindingUA (p: Render.ShaderParameter) (value: obj) (stage: ComputeShaderStage) =
        match p.Binding with
        | Render.ShaderParameterBinding.UnorderedAccess -> stage.SetUnorderedAccessView(p.Register, unbox value)
        | _ -> updateBinding p value stage

// shader context; it can be used for setting shaders and shader parameters
// note: there is no internal queueing of commands - all calls update the device context state immediately
// this can lead to excessive setup, but removes the need for dirty flags and per-drawcall flush
type ShaderContext(cbPool: ConstantBufferPool, context: DeviceContext) =
    let values = List<obj>()
    let cbslots = List<ConstantBuffer>()
    let vertexParams = List<Render.ShaderParameter>()
    let pixelParams = List<Render.ShaderParameter>()
    let computeParams = List<Render.ShaderParameter>()

    // disposes of all constant buffers, releasing them into pool
    interface IDisposable with
        member this.Dispose() =
            for p in cbslots do
                (p :> IDisposable).Dispose()

    // make sure that slot id represents a valid slot
    member private this.EnsureSlot(slot) =
        while values.Count <= slot do
            values.Add(null)
            vertexParams.Add(Render.ShaderParameter())
            pixelParams.Add(Render.ShaderParameter())
            computeParams.Add(Render.ShaderParameter())

    // update device context binding to match slot values
    member private this.ValidateSlot(slot) =
        ShaderUtil.updateBinding vertexParams.[slot] values.[slot] context.VertexShader
        ShaderUtil.updateBinding pixelParams.[slot] values.[slot] context.PixelShader
        ShaderUtil.updateBindingUA computeParams.[slot] values.[slot] context.ComputeShader

    // set a new value into slot
    member private this.UpdateSlot(slot, value) =
        this.EnsureSlot(slot)
        values.[slot] <- value
        this.ValidateSlot(slot)

    // currently bound program object
    member this.Program
        with set (value: Render.Program) =
            context.ComputeShader.Set(value.ComputeShader.Resource)

            // clear all previous bindings of values to parameters ($$ revise: causes excessive setup in case of shared parameters)
            for slot = 0 to values.Count - 1 do
                computeParams.[slot] <- Render.ShaderParameter()

            // update comput shader bindings
            for p in value.ComputeShader.Parameters do
                this.EnsureSlot(p.Slot)
                computeParams.[p.Slot] <- p

            // make sure all previously set values are synchronized with device context
            for slot = 0 to values.Count - 1 do
                if values.[slot] <> null then
                    this.ValidateSlot(slot)

    // currently bound shader object
    member this.Shader
        with set (value: Render.Shader) =
            context.VertexShader.Set(value.VertexShader.Resource)
            context.PixelShader.Set(value.PixelShader.Resource)

            // clear all previous bindings of values to parameters ($$ revise: causes excessive setup in case of shared parameters)
            for slot = 0 to values.Count - 1 do
                vertexParams.[slot] <- Render.ShaderParameter()
                pixelParams.[slot] <- Render.ShaderParameter()

            // update vertex shader bindings
            for p in value.VertexShader.Parameters do
                this.EnsureSlot(p.Slot)
                vertexParams.[p.Slot] <- p

            // update pixel shader bindings
            for p in value.PixelShader.Parameters do
                this.EnsureSlot(p.Slot)
                pixelParams.[p.Slot] <- p

            // make sure all previously set values are synchronized with device context
            for slot = 0 to values.Count - 1 do
                if values.[slot] <> null then
                    this.ValidateSlot(slot)

    // set value to slot by index
    member this.SetConstant(slot, value: ShaderResourceView) =
        this.UpdateSlot(slot, value)

    // set value to slot by index
    member this.SetConstant(slot, value: UnorderedAccessView) =
        this.UpdateSlot(slot, value)

    // set value to slot by index
    member this.SetConstant(slot, value: SamplerState) =
        this.UpdateSlot(slot, value)

    // set value to slot by index
    member this.SetConstant(slot, value: Buffer) =
        this.UpdateSlot(slot, value)

    // set value to slot by index
    member this.SetConstant(slot, value: obj) =
        while cbslots.Count <= slot do cbslots.Add(Unchecked.defaultof<_>)

        (cbslots.[slot] :> IDisposable).Dispose()

        let data = ShaderUtil.uploadConstantData cbPool context value
        this.UpdateSlot(slot, data.Buffer)
        cbslots.[slot] <- data

    // set value to slot by name
    static member (?<-) (this: ShaderContext, name: string, value: ShaderResourceView) =
        this.SetConstant(Render.ShaderParameterRegistry.getSlot name, value)

    // set value to slot by name
    static member (?<-) (this: ShaderContext, name: string, value: UnorderedAccessView) =
        this.SetConstant(Render.ShaderParameterRegistry.getSlot name, value)

    // set value to slot by name
    static member (?<-) (this: ShaderContext, name: string, value: SamplerState) =
        this.SetConstant(Render.ShaderParameterRegistry.getSlot name, value)

    // set value to slot by name
    static member (?<-) (this: ShaderContext, name: string, value: Buffer) =
        this.SetConstant(Render.ShaderParameterRegistry.getSlot name, value)

    // set value to slot by name
    static member (?<-) (this: ShaderContext, name: string, value: obj) =
        this.SetConstant(Render.ShaderParameterRegistry.getSlot name, value)