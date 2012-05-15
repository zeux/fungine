namespace Math

[<Struct>]
type Sphere(center: Vector3, radius: float) =
    // center/radius accessors
    member this.Center = center
    member this.Radius = radius