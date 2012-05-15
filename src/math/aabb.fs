namespace Math

[<Struct>]
type AABB(min: Vector3, max: Vector3) =
    // min/max accessors
    member this.Min = min
    member this.Max = max

    // center/extent accessors; extent is half the box dimensions
    member this.Center = (min + max) / 2.f
    member this.Extent = (max - min) / 2.f