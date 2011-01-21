namespace Math

open System.Runtime.InteropServices

#nowarn "9" // Uses of this construct may result in the generation of unverifiable .NET IL code

[<Struct; StructLayout(LayoutKind.Sequential, Pack = 4)>]
type Matrix34 =
    val mutable row0: Vector4
    val mutable row1: Vector4
    val mutable row2: Vector4

    // base ctor
    new (row0, row1, row2) = { row0 = row0; row1 = row1; row2 = row2 }

    // ctor from elements
    new (m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23) =
        { row0 = Vector4(m00, m01, m02, m03);
          row1 = Vector4(m10, m11, m12, m13);
          row2 = Vector4(m20, m21, m22, m23) }

    // index operators
    member this.Row index =
        match index with
        | 0 -> this.row0
        | 1 -> this.row1
        | 2 -> this.row2
        | _ -> raise (System.IndexOutOfRangeException())

    member this.Column index =
        match index with
        | 0 -> Vector3(this.row0.x, this.row1.x, this.row2.x)
        | 1 -> Vector3(this.row0.y, this.row1.y, this.row2.y)
        | 2 -> Vector3(this.row0.z, this.row1.z, this.row2.z)
        | 3 -> Vector3(this.row0.w, this.row1.w, this.row2.w)
        | _ -> raise (System.IndexOutOfRangeException())

    member this.Item row column = (this.Row row).[column]

    // arithmetic operators (unary)
    static member (~+) (m: Matrix34) = m
    static member (~-) (m: Matrix34) = Matrix34(-m.row0, -m.row1, -m.row2)

    // arithmetic operators (binary, matrix)
    static member (+) (l: Matrix34, r: Matrix34) = Matrix34(l.row0 + r.row0, l.row1 + r.row1, l.row2 + r.row2)
    static member (-) (l: Matrix34, r: Matrix34) = Matrix34(l.row0 - r.row0, l.row1 - r.row1, l.row2 - r.row2)

    // arithmetic operators (binary, scalar)
    static member (*) (l: float32, r: Matrix34) = Matrix34(l * r.row0, l * r.row1, l * r.row2)
    static member (*) (l: Matrix34, r: float32) = Matrix34(l.row0 * r, l.row1 * r, l.row2 * r)
    static member (/) (l: Matrix34, r: float32) = Matrix34(l.row0 / r, l.row1 / r, l.row2 / r)

    // string representation
    override this.ToString() = sprintf "%A\n%A\n%A" this.row0 this.row1 this.row2