namespace Render

open SlimDX.Direct3D11

// texture
type Texture(resource: Texture2D, view: ShaderResourceView) =
    // get texture resource
    member this.Resource = resource

    // get texture view
    member this.View = view

// texture loader
module TextureLoader =
    let load device path =
        // create load information for the entire mip chain
        let image = ImageInformation.FromFile(path)
        let mutable info = ImageLoadInformation.FromDefaults()
        info.MipLevels <- image.Value.MipLevels

        // load texture
        let resource = Texture2D.FromFile(device, path, info)

        // create default view
        let view = new ShaderResourceView(device, resource)

        Texture(resource, view)