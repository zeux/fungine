namespace Math

open System.Runtime.InteropServices

#nowarn "9" // Uses of this construct may result in the generation of unverifiable .NET IL code

[<Struct; StructLayout(LayoutKind.Sequential, Pack = 4)>]
type Vector3 =
    val x: float32
    val y: float32
    val z: float32

    // base ctor
    new (x, y, z) = { x = x; y = y; z = z }

    // ctor from Vector2
    new (v: Vector2, z) = { x = v.x; y = v.y; z = z }

    // index operator
    member this.Item index =
        match index with
        | 0 -> this.x
        | 1 -> this.y
        | 2 -> this.z
        | _ -> raise (System.IndexOutOfRangeException())

    // arithmetic operators (unary)
    static member (~+) (v: Vector3) = v
    static member (~-) (v: Vector3) = Vector3(-v.x, -v.y, -v.z)

    // arithmetic operators (binary, vector)
    static member (+) (l: Vector3, r: Vector3) = Vector3(l.x + r.x, l.y + r.y, l.z + r.z)
    static member (-) (l: Vector3, r: Vector3) = Vector3(l.x - r.x, l.y - r.y, l.z - r.z)
    static member (*) (l: Vector3, r: Vector3) = Vector3(l.x * r.x, l.y * r.y, l.z * r.z)
    static member (/) (l: Vector3, r: Vector3) = Vector3(l.x / r.x, l.y / r.y, l.z / r.z)

    // arithmetic operators (binary, scalar)
    static member (*) (l: float32, r: Vector3) = Vector3(l * r.x, l * r.y, l * r.z)
    static member (*) (l: Vector3, r: float32) = Vector3(l.x * r, l.y * r, l.z * r)
    static member (/) (l: float32, r: Vector3) = Vector3(l / r.x, l / r.y, l / r.z)
    static member (/) (l: Vector3, r: float32) = Vector3(l.x / r, l.y / r, l.z / r)

    // length
    member this.LengthSquared = this.x * this.x + this.y * this.y + this.z * this.z
    member this.Length = sqrt this.LengthSquared

    // dot product
    static member Dot (l: Vector3, r: Vector3) = l.x * r.x + l.y * r.y + l.z * r.z
    
    // cross product
    static member Cross (l: Vector3, r: Vector3) =
        Vector3(l.y * r.z - l.z * r.y,
                l.z * r.x - l.x * r.z,
                l.x * r.y - l.y * r.x)

    // normalize
    static member Normalize (v: Vector3) =
        let length = v.Length
        if length = 0.f then v else v / length

    // distance
    static member Distance (l: Vector3, r: Vector3) = (l - r).Length
    static member DistanceSquared (l: Vector3, r: Vector3) = (l - r).LengthSquared

    // per-component min/max
    static member Minimize (l: Vector3, r: Vector3) = Vector3(min l.x r.x, min l.y r.y, min l.z r.z)
    static member Maximize (l: Vector3, r: Vector3) = Vector3(max l.x r.x, max l.y r.y, max l.z r.z)

    // lerp
    static member Lerp (l: Vector3, r: Vector3, k: float32) = l + (r - l) * k

    // string representation
    override this.ToString() = sprintf "{%f %f %f}" this.x this.y this.z