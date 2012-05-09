namespace Render.Lighting

open Render

[<ShaderStruct>]
type LightType =
    | Directional = 0
    | Point = 1
    | Spot = 2

[<ShaderStruct>]
type LightCullData(typ: LightType, position: Vector3, radius: float32) =
    member this.Type = typ
    member this.Position = position
    member this.Radius = radius

[<ShaderStruct>]
type LightShadowData(transform: Matrix44, atlasScale: Vector2, atlasOffset: Vector2) =
    member this.Transform = transform
    member this.AtlasScale = atlasScale
    member this.AtlasOffset = atlasOffset

[<ShaderStruct>]
type LightData(typ: LightType, position: Vector3, direction: Vector3, radius: float32, outerAngle: float32, innerAngle: float32, color: Color4, intensity: float32, shadowData: LightShadowData) =
    member this.Type = typ
    member this.Position = position
    member this.Direction = direction
    member this.Radius = radius
    member this.OuterAngle = outerAngle
    member this.InnerAngle = innerAngle
    member this.Color = color
    member this.Intensity = intensity
    member this.ShadowData = shadowData
