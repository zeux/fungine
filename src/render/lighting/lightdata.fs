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
type LightShadowCascade(transformScale: Vector2, transformOffset: Vector2, atlasScale: Vector2, atlasOffset: Vector2) =
    member this.TransformScale = transformScale
    member this.TransformOffset = transformOffset
    member this.AtlasScale = atlasScale
    member this.AtlasOffset = atlasOffset

[<ShaderStruct>]
type LightShadowData(transform: Matrix44, cascadeDistances: Vector4, cascadeInfo: LightShadowCascade array) =
    member this.Transform = transform
    member this.CascadeDistances = cascadeDistances
    member this.CascadeInfo0 = cascadeInfo.[0]
    member this.CascadeInfo1 = cascadeInfo.[if cascadeInfo.Length <= 1 then 0 else 1]
    member this.CascadeInfo2 = cascadeInfo.[if cascadeInfo.Length <= 2 then 0 else 2]
    member this.CascadeInfo3 = cascadeInfo.[if cascadeInfo.Length <= 3 then 0 else 3]

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
