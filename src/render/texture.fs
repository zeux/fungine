namespace Render

open SlimDX.Direct3D11

// texture resource data
[<AllowNullLiteral>]
type private TextureData(path) =
    // load texture from file
    let resource = Texture2D.FromFile(Render.Device.get(), path)
    let view = new ShaderResourceView(Render.Device.get(), resource)

    // texture resource
    member this.Resource = resource

    // texture view
    member this.View = view

// texture resource cache
module private TextureCache =
    let cache = Core.ConcurrentCache(fun path -> TextureData(path))

// texture handle
type Texture(path) =
    // texture data
    [<System.NonSerialized>]
    let mutable data = null

    // lazy initializing data accessor ($$ replace with post-serialization callback)
    member private this.Data =
        if data = null then
            data <- TextureCache.cache.Get path
        data

    // get texture path
    member this.Path = path

    // get texture resource
    member this.Resource = this.Data.Resource

    // get texture view
    member this.View = this.Data.View

    // convert to string
    override this.ToString () = path
