namespace Math

[<Struct>]
type Frustum(planes: Vector4 array) =
    // ctor from projection matrix
    new (proj: Matrix44) =
        let normalize (p: Vector4) = p / p.xyz.Length

        Frustum [|
            normalize proj.row2
            normalize (proj.row3 - proj.row2)
            normalize (proj.row3 - proj.row0)
            normalize (proj.row3 + proj.row0)
            normalize (proj.row3 - proj.row1)
            normalize (proj.row3 + proj.row1) |]

    // planes accessor
    member this.Planes = planes