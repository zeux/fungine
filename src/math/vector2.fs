namespace Math

open System.Runtime.InteropServices

#nowarn "9" // Uses of this construct may result in the generation of unverifiable .NET IL code

[<Struct; StructLayout(LayoutKind.Sequential, Pack = 4)>]
type Vector2 =
    val mutable x: float32
    val mutable y: float32

    // ctor
    new (x, y) = { x = x; y = y }

    // index operator
    member this.Item index =
        match index with
        | 0 -> this.x
        | 1 -> this.y
        | _ -> raise (System.IndexOutOfRangeException())

    // arithmetic operators (unary)
    static member (~+) (v: Vector2) = v
    static member (~-) (v: Vector2) = Vector2(-v.x, -v.y)

    // arithmetic operators (binary, vector)
    static member (+) (l: Vector2, r: Vector2) = Vector2(l.x + r.x, l.y + r.y)
    static member (-) (l: Vector2, r: Vector2) = Vector2(l.x - r.x, l.y - r.y)
    static member (*) (l: Vector2, r: Vector2) = Vector2(l.x * r.x, l.y * r.y)
    static member (/) (l: Vector2, r: Vector2) = Vector2(l.x / r.x, l.y / r.y)

    // arithmetic operators (binary, scalar)
    static member (*) (l: float32, r: Vector2) = Vector2(l * r.x, l * r.y)
    static member (*) (l: Vector2, r: float32) = Vector2(l.x * r, l.y * r)
    static member (/) (l: float32, r: Vector2) = Vector2(l / r.x, l / r.y)
    static member (/) (l: Vector2, r: float32) = Vector2(l.x / r, l.y / r)

    // length
    member this.LengthSquared = this.x * this.x + this.y * this.y
    member this.Length = sqrt this.LengthSquared

    // dot product
    static member Dot (l: Vector2, r: Vector2) = l.x * r.x + l.y * r.y

    // cross product
    static member Cross (l: Vector2, r: Vector2) = l.x * r.y - l.y * r.x

    // normalize
    static member Normalize (v: Vector2) =
        let length = v.Length
        if length = 0.f then v else v / length

    // distance
    static member Distance (l: Vector2, r: Vector2) = (l - r).Length
    static member DistanceSquared (l: Vector2, r: Vector2) = (l - r).LengthSquared

    // per-component min/max
    static member Minimize (l: Vector2, r: Vector2) = Vector2(min l.x r.x, min l.y r.y)
    static member Maximize (l: Vector2, r: Vector2) = Vector2(max l.x r.x, max l.y r.y)

    // lerp
    static member Lerp (l: Vector2, r: Vector2, k: float32) = l + (r - l) * k

    // string representation
    override this.ToString() = sprintf "%f %f" this.x this.y