namespace Render

open System
open System.Collections.Generic
open SlimDX
open SlimDX.Direct3D11

// MSAA type
type MSAA =
    | None = 1
    | X2 = 2
    | X4 = 4
    | X6 = 6
    | X8 = 8

// render target owner
type private RenderTargetOwner = string

// render target slot
type private RenderTargetSlot =
    { desc: Texture2DDescription
      target: RenderTarget
      mutable owner: RenderTargetOwner option }

    // debug print
    override this.ToString () =
        let desc = sprintf "RenderTargetSlot %dx%d %A %dxAA" this.desc.Width this.desc.Height this.desc.Format this.desc.SampleDescription.Count
        match this.owner with
        | Some o -> sprintf "%s {%s}" o desc
        | None -> desc

// render target
and RenderTarget(resource: Resource, view: ShaderResourceView, color_view: RenderTargetView, depth_view: DepthStencilView, ?pool: RenderTargetPool) =
    // release target to the pool via dispose, if it's bound to a pool; dispose of resources otherwise
    interface IDisposable with
        member this.Dispose() =
            match pool with
            | Some p -> p.Release(this)
            | None ->
                // this is a hack - ideally we should have a separate type for pool-bound RT
                resource.Dispose()
                if view <> null then view.Dispose()
                if color_view <> null then color_view.Dispose()
                if depth_view <> null then depth_view.Dispose()

    // get texture resource
    member this.Resource = resource

    // get texture view
    member this.View = view

    // get color target view
    member this.ColorView = color_view

    // get depth target view
    member this.DepthView = depth_view

// render target pool
and RenderTargetPool(device) =
    // render target cache
    let cache = List<RenderTargetSlot>()

    // create color render target by description
    member private this.CreateColor (desc: Texture2DDescription) =
        // create resource
        let resource = new Texture2D(device, desc)

        // create views
        let view = new ShaderResourceView(device, resource)
        let color_view = new RenderTargetView(device, resource)

        // create target object
        new RenderTarget(resource, view, color_view, null, this)

    // create depth render target by description
    member private this.CreateDepth (desc: Texture2DDescription) =
        // get typeless and shader versions of the format
        let (resource_format, shader_format) =
            match desc.Format with
            | Format.D32_Float_S8X24_UInt -> Format.R32G8X24_Typeless, Format.R32_Float_X8X24_Typeless
            | Format.D32_Float -> Format.R32_Typeless, Format.R32_Float
            | Format.D24_UNorm_S8_UInt -> Format.R24G8_Typeless, Format.R24_UNorm_X8_Typeless
            | Format.D16_UNorm -> Format.R16_Typeless, Format.R16_UNorm
            | _ -> failwithf "Unknown depth format %A" desc.Format

        // get resource dimensions
        let (shader_dimension, depth_dimension) =
            if desc.SampleDescription.Count = 1 then
                ShaderResourceViewDimension.Texture2D, DepthStencilViewDimension.Texture2D
             else
                ShaderResourceViewDimension.Texture2DMultisampled, DepthStencilViewDimension.Texture2DMultisampled

        // create resource
        let mutable rdesc = desc
        rdesc.Format <- resource_format

        let resource = new Texture2D(device, rdesc)

        // create views
        let view = new ShaderResourceView(device, resource, ShaderResourceViewDescription(Format = shader_format, MipLevels = desc.MipLevels, Dimension = shader_dimension))
        let depth_view = new DepthStencilView(device, resource, DepthStencilViewDescription(Format = desc.Format, Dimension = depth_dimension))

        // create target object
        new RenderTarget(resource, view, null, depth_view, this)

    // acquire target by description
    member this.Acquire (owner, desc) =
        // if there's a matching unused slot, return it
        match cache |> Seq.tryFind (fun s -> s.owner.IsNone && s.desc = desc) with
        | Some slot ->
            slot.owner <- Some owner
            slot.target
        | None ->
            // create target by description
            let target = if Formats.isDepth desc.Format then this.CreateDepth desc else this.CreateColor desc

            // add target to cache so that we can use it later
            let slot = { new RenderTargetSlot with desc = desc and target = target and owner = Some owner }
            cache.Add(slot)

            target

    // simplified acquire interface
    member this.Acquire (owner, width, height, format, ?miplevels, ?samples) =
        let bind_flags = BindFlags.ShaderResource ||| (if Formats.isDepth format then BindFlags.DepthStencil else BindFlags.RenderTarget)
        let desc = Texture2DDescription(Width = width, Height = height, MipLevels = defaultArg miplevels 1, ArraySize = 1, Format = format,
                    SampleDescription = DXGI.SampleDescription(int (defaultArg samples MSAA.None), 0), Usage = ResourceUsage.Default, BindFlags = bind_flags)
        this.Acquire(owner, desc)

    // release target
    member this.Release(target) =
        let index = cache |> Seq.findIndex (fun s -> s.target = target)

        assert (cache.[index].owner.IsSome)
        cache.[index].owner <- None

    // release unused
    member this.ReleaseUnused () =
        cache.RemoveAll(fun slot -> slot.owner.IsNone) |> ignore