namespace Render.Lighting

open SharpDX.Direct3D11

open Render

// light grid with an array of 2-byte light indices per tile
[<ShaderStruct>]
type LightGrid(device: Device, widthPixels, heightPixels, maxLightsPerTile) =
    static let cellSize = 16 // cell size is a fixed value because of CS restrictions

    let width = (widthPixels + cellSize - 1) / cellSize
    let height = (heightPixels + cellSize - 1) / cellSize

    let format = Format.R16_UInt

    let indexSize = Formats.getSizeBits format / 8
    let indexCount = width * height * maxLightsPerTile

    let buffer =
        new Buffer(device.Device, indexSize * indexCount, ResourceUsage.Default, BindFlags.UnorderedAccess ||| BindFlags.ShaderResource,
            CpuAccessFlags.None, ResourceOptionFlags.None, 0)

    let view =
        new ShaderResourceView(device.Device, buffer,
            ShaderResourceViewDescription(Format = format, Dimension = ShaderResourceViewDimension.Buffer, Buffer =
                ShaderResourceViewDescription.BufferResource(ElementCount = indexCount)))

    let uaView =
        new UnorderedAccessView(device.Device, buffer,
            UnorderedAccessViewDescription(Format = format, Dimension = UnorderedAccessViewDimension.Buffer, Buffer =
                UnorderedAccessViewDescription.BufferResource(ElementCount = indexCount)))

    // get grid dimensions
    static member CellSize = cellSize

    member this.Width = width
    member this.Height = height
    member this.TileSize = maxLightsPerTile

    // get grid stride in elements
    member this.Stride = width * maxLightsPerTile

    // get read-only view
    member this.View = view

    // get unordered view
    member this.UnorderedView = uaView