[<AutoOpen>]
module MathSwizzles

open global.Math

type Vector3 with
    member this.xy = Vector2(this.x, this.y)

type Vector4 with
    member this.xy = Vector2(this.x, this.y)
    member this.xyz = Vector3(this.x, this.y, this.z)
