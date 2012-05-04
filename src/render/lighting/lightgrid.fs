namespace Render.Lighting

open SlimDX.Direct3D11

open Render

[<ShaderStruct>]
type LightData(position: Vector3, direction: Vector3, radius: float32, outerAngle: float32, innerAngle: float32, color: Color4, intensity: float32) =
    member this.Position = position
    member this.Direction = direction
    member this.Radius = radius
    member this.OuterAngle = outerAngle
    member this.InnerAngle = innerAngle
    member this.Color = color
    member this.Intensity = intensity

// light grid with an array of 2-byte light indices per tile
[<ShaderStruct>]
type LightGrid(device: Device, widthPixels, heightPixels, cellSize, maxLightsPerTile) =
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
            ShaderResourceViewDescription(Format = format, Dimension = ShaderResourceViewDimension.Buffer, ElementWidth = indexCount))

    let uaView =
        new UnorderedAccessView(device.Device, buffer,
            UnorderedAccessViewDescription(Format = format, Dimension = UnorderedAccessViewDimension.Buffer, ElementCount = indexCount))

    // get grid dimensions
    member this.Width = width
    member this.Height = height
    member this.CellSize = cellSize
    member this.TileSize = maxLightsPerTile

    // get grid stride in elements
    member this.Stride = width * maxLightsPerTile

    // get read-only view
    member this.View = view

    // get unordered view
    member this.UnorderedView = uaView