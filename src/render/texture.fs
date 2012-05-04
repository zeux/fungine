namespace Render

open System
open System.IO
open System.Runtime.InteropServices

open SharpDX.Data
open SharpDX.Direct3D11

#nowarn "9" // Uses of this construct may result in the generation of unverifiable .NET IL code

// texture
type Texture(resource: Resource, view: ShaderResourceView) =
    // get texture resource
    member this.Resource = resource

    // get texture view
    member this.View = view

// D3DX texture loader
#if D3DX
module private TextureLoaderD3DX =
    // load texture from file
    let load device path = // create load information for the entire mip chain
        let image = ImageInformation.FromFile(path)
        let info =
            ImageLoadInformation(
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                Width = image.Value.Width,
                Height = image.Value.Height,
                Depth = image.Value.Depth,
                MipLevels = image.Value.MipLevels,
                Filter = FilterFlags.None,
                Format = image.Value.Format,
                OptionFlags = image.Value.OptionFlags)

        // load texture
        let resource =
            match image.Value.ResourceDimension with
            | ResourceDimension.Texture2D -> Texture2D.FromFile(device, path, info)
            | ResourceDimension.Texture3D -> Texture3D.FromFile(device, path, info)
            | d -> failwithf "Unsupported image dimension %A" d

        // create default view
        let view = new ShaderResourceView(device, resource)

        Texture(resource, view)
#endif

// DDS texture loader
module private TextureLoaderDDS =
    // pixel format
    [<Struct; StructLayout(LayoutKind.Sequential, Pack = 4)>]
    type PixelFormat =
        val size: int
        val flags: int
        val fourCC: int
        val rgbBitCount: int
        val rBitMask: int
        val gBitMask: int
        val bBitMask: int
        val aBitMask: int

    // reserved block
    [<Struct; StructLayout(LayoutKind.Sequential, Size = 44)>]
    type Reserved11 = struct end

    // header
    [<Struct; StructLayout(LayoutKind.Sequential, Pack = 4)>]
    type Header =
        val size: int
        val flags: int
        val height: int
        val width: int
        val pitch: int
        val depth: int
        val miplevels: int
        val reserved1: Reserved11
        val format: PixelFormat
        val caps: int
        val caps2: int
        val caps3: int
        val caps4: int
        val reserved2: int

    // additional header for DX10
    [<Struct; StructLayout(LayoutKind.Sequential, Pack = 4)>]
    type HeaderDX10 =
        val format: Format
        val dimension: ResourceDimension
        val flags: ResourceOptionFlags
        val arraySize: int
        val reserved: int

        // ctor
        new (format, dimension, flags, arraySize) =
            { format = format; dimension = dimension; flags = flags; arraySize = arraySize; reserved = 0 }

    // miscellaneous flags
    let DDSCAPS2_CUBEMAP = 0x200
    let DDSCAPS2_CUBEMAP_FACES = 0xfc00
    let DDSCAPS2_VOLUME = 0x200000

    // FourCC helper
    let getFourCC (data: string) = BitConverter.ToInt32(data.ToCharArray() |> Array.map byte, 0)

    // get texture format
    let getFormat (format: PixelFormat) =
        match format.fourCC with
        | 0 ->
            if format.rgbBitCount = 32 && format.bBitMask = 0xff0000 && format.gBitMask = 0xff00 && format.rBitMask = 0xff &&
               (format.aBitMask = 0xff000000 || format.aBitMask = 0) then Format.R8G8B8A8_UNorm
            elif format.rgbBitCount = 8 && format.rBitMask = 0xff then Format.R8_UNorm
            else failwithf "Unknown RGBA mask combination: %d bpp (R=%x G=%x B=%x A=%x)" format.rgbBitCount format.rBitMask format.gBitMask format.bBitMask format.aBitMask
        | 111 -> Format.R16_Float
        | 112 -> Format.R16G16_Float
        | 113 -> Format.R16G16B16A16_Float
        | 114 -> Format.R32_Float
        | 115 -> Format.R32G32_Float
        | 116 -> Format.R32G32B32A32_Float
        | x when x = getFourCC "DXT1" -> Format.BC1_UNorm
        | x when x = getFourCC "DXT3" -> Format.BC2_UNorm
        | x when x = getFourCC "DXT5" -> Format.BC3_UNorm
        | x when x = getFourCC "ATI1" -> Format.BC4_UNorm
        | x when x = getFourCC "ATI2" -> Format.BC5_UNorm
        | _ -> failwithf "Unknown FourCC %x" format.fourCC

    // get DX10-style header from DX9-style header
    let getHeaderDX10 (header: Header) =
        let format = getFormat header.format

        if (header.caps2 &&& DDSCAPS2_VOLUME) <> 0 then
            HeaderDX10(format, ResourceDimension.Texture3D, ResourceOptionFlags.None, 1)
        elif (header.caps2 &&& DDSCAPS2_CUBEMAP) <> 0 then
            if (header.caps2 &&& DDSCAPS2_CUBEMAP_FACES) <> DDSCAPS2_CUBEMAP_FACES then failwithf "Partial cubemap textures are not supported"
            HeaderDX10(format, ResourceDimension.Texture2D, ResourceOptionFlags.TextureCube, 1)
        else
            HeaderDX10(format, ResourceDimension.Texture2D, ResourceOptionFlags.None, 1)

    // get mip size
    let getMipSize size mip =
        max (size >>> mip) 1

    // get row pitch
    let getRowPitch format width =
        (if Formats.isBlockCompressed format then (width + 3) / 4 else width) * Formats.getSizeBits format / 8

    // get slice pitch
    let getSlicePitch format width height =
        getRowPitch format width * (if Formats.isBlockCompressed format then (height + 3) / 4 else height)

    // get texture subresource layout
    let getLayout format width height depth miplevels arraysize conv =
        let offset = ref 0
        Array.init (arraysize * miplevels) (fun idx ->
            let mip = idx % miplevels
            let rowPitch = getRowPitch format (getMipSize width mip)
            let slicePitch = getSlicePitch format (getMipSize width mip) (getMipSize height mip)
            let size = slicePitch * getMipSize depth mip
            offset := !offset + size
            conv rowPitch slicePitch size (!offset - size))

    // create texture
    let createTexture device (header: Header) (header10: HeaderDX10) streamc =
        let miplevels = max header.miplevels 1

        match header10.dimension with
        | ResourceDimension.Texture2D ->
            let asmult = if header10.flags.HasFlag(ResourceOptionFlags.TextureCube) then 6 else 1
            let desc = Texture2DDescription(Width = header.width, Height = header.height, MipLevels = miplevels, ArraySize = header10.arraySize * asmult,
                        Format = header10.format, SampleDescription = SampleDescription(1, 0), Usage = ResourceUsage.Immutable, BindFlags = BindFlags.ShaderResource,
                        OptionFlags = header10.flags)
            let conv rowPitch slicePitch size offset = DataRectangle(streamc offset size, rowPitch)
            new Texture2D(device, desc, getLayout desc.Format desc.Width desc.Height 1 desc.MipLevels desc.ArraySize conv) :> Resource
        | ResourceDimension.Texture3D ->
            let desc = Texture3DDescription(Width = header.width, Height = header.height, Depth = header.depth, MipLevels = miplevels,
                        Format = header10.format, Usage = ResourceUsage.Immutable, BindFlags = BindFlags.ShaderResource)
            let conv rowPitch slicePitch size offset = DataBox(streamc offset size, rowPitch, slicePitch)
            new Texture3D(device, desc, getLayout desc.Format desc.Width desc.Height desc.Depth desc.MipLevels 1 conv) :> Resource
        | d -> failwithf "Unsupported image dimension %A" d

    // load file in memory
    let loadFile path =
        use file = File.OpenRead(path)
        let data = new DataStream(int file.Length, canRead = true, canWrite = true)
        file.CopyTo(data)
        data.Position <- 0L
        data

    // load texture from file
    let load device path =
        use data = loadFile path

        // read header
        if data.Read<int>() <> getFourCC "DDS " then failwith "Unrecognized header: incorrect magic"

        let header = data.Read<Header>()
        let header10 = if header.format.fourCC = getFourCC "DX10" then data.Read<HeaderDX10>() else getHeaderDX10 header

        // get source data stream
        let dataOffset = 4 + header.size + (if header.format.fourCC = getFourCC "DX10" then sizeof<HeaderDX10> else 0)

        let streamc offset size =
            if int64 (dataOffset + offset + size) > data.Length then failwith "Error reading image data: file is truncated"
            data.DataPointer + nativeint (dataOffset + offset)

        // create texture resource
        let resource = createTexture device header header10 streamc

        // create default view
        let view = new ShaderResourceView(device, resource)

        Texture(resource, view)

// texture loader
module TextureLoader =
    // load texture from file
    let load device path =
        TextureLoaderDDS.load device path