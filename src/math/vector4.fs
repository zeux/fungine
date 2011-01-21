namespace Math

open System.Runtime.InteropServices

#nowarn "9" // Uses of this construct may result in the generation of unverifiable .NET IL code

[<Struct; StructLayout(LayoutKind.Sequential, Pack = 4)>]
type Vector4 =
    val mutable x: float32
    val mutable y: float32
    val mutable z: float32
    val mutable w: float32

    // base ctor
    new (x, y, z, w) = { x = x; y = y; z = z; w = w }

    // ctor from Vector3
    new (v: Vector3, w) = { x = v.x; y = v.y; z = v.z; w = w }

    // index operator
    member this.Item index =
        match index with
        | 0 -> this.x
        | 1 -> this.y
        | 2 -> this.z
        | 3 -> this.w
        | _ -> raise (System.IndexOutOfRangeException())

    // arithmetic operators (unary)
    static member (~+) (v: Vector4) = v
    static member (~-) (v: Vector4) = Vector4(-v.x, -v.y, -v.z, -v.w)

    // arithmetic operators (binary, vector)
    static member (+) (l: Vector4, r: Vector4) = Vector4(l.x + r.x, l.y + r.y, l.z + r.z, l.w + r.w)
    static member (-) (l: Vector4, r: Vector4) = Vector4(l.x - r.x, l.y - r.y, l.z - r.z, l.w - r.w)
    static member (*) (l: Vector4, r: Vector4) = Vector4(l.x * r.x, l.y * r.y, l.z * r.z, l.w * r.w)
    static member (/) (l: Vector4, r: Vector4) = Vector4(l.x / r.x, l.y / r.y, l.z / r.z, l.w / r.w)

    // arithmetic operators (binary, scalar)
    static member (*) (l: float32, r: Vector4) = Vector4(l * r.x, l * r.y, l * r.z, l * r.w)
    static member (*) (l: Vector4, r: float32) = Vector4(l.x * r, l.y * r, l.z * r, l.w * r)
    static member (/) (l: float32, r: Vector4) = Vector4(l / r.x, l / r.y, l / r.z, l / r.w)
    static member (/) (l: Vector4, r: float32) = Vector4(l.x / r, l.y / r, l.z / r, l.w / r)

    // dot product
    static member Dot (l: Vector4, r: Vector4) = l.x * r.x + l.y * r.y + l.z * r.z + l.w * r.w

    // per-component min/max
    static member Minimize (l: Vector4, r: Vector4) = Vector4(min l.x r.x, min l.y r.y, min l.z r.z, min l.w r.w)
    static member Maximize (l: Vector4, r: Vector4) = Vector4(max l.x r.x, max l.y r.y, max l.z r.z, max l.w r.w)

    // lerp
    static member Lerp (l: Vector4, r: Vector4, k: float32) = l + (r - l) * k

    // string representation
    override this.ToString() = sprintf "%f %f %f %f" this.x this.y this.z this.w

    // constants
    static member Zero = Vector4()
    static member UnitX = Vector4(1.f, 0.f, 0.f, 0.f)
    static member UnitY = Vector4(0.f, 1.f, 0.f, 0.f)
    static member UnitZ = Vector4(0.f, 0.f, 1.f, 0.f)
    static member UnitW = Vector4(0.f, 0.f, 0.f, 1.f)