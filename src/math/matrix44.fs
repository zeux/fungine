namespace Math

open System.Runtime.InteropServices

#nowarn "9" // Uses of this construct may result in the generation of unverifiable .NET IL code

[<Struct; StructLayout(LayoutKind.Sequential, Pack = 4)>]
type Matrix44 =
    val mutable row0: Vector4
    val mutable row1: Vector4
    val mutable row2: Vector4
    val mutable row3: Vector4

    // base ctor
    new (row0, row1, row2, row3) = { row0 = row0; row1 = row1; row2 = row2; row3 = row3 }

    // ctor from elements
    new (m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33) =
        { row0 = Vector4(m00, m01, m02, m03);
          row1 = Vector4(m10, m11, m12, m13);
          row2 = Vector4(m20, m21, m22, m23);
          row3 = Vector4(m30, m31, m32, m33) }

    // ctor from Matrix34
    new (m: Matrix34) = { row0 = m.row0; row1 = m.row1; row2 = m.row2; row3 = Vector4.UnitW }

    // index operators
    member this.Row index =
        match index with
        | 0 -> this.row0
        | 1 -> this.row1
        | 2 -> this.row2
        | 3 -> this.row3
        | _ -> raise (System.IndexOutOfRangeException())

    member this.Column index =
        match index with
        | 0 -> Vector4(this.row0.x, this.row1.x, this.row2.x, this.row3.x)
        | 1 -> Vector4(this.row0.y, this.row1.y, this.row2.y, this.row3.y)
        | 2 -> Vector4(this.row0.z, this.row1.z, this.row2.z, this.row3.z)
        | 3 -> Vector4(this.row0.w, this.row1.w, this.row2.w, this.row3.w)
        | _ -> raise (System.IndexOutOfRangeException())

    member this.Item row column = (this.Row row).[column]

    // arithmetic operators (unary)
    static member (~+) (m: Matrix44) = m
    static member (~-) (m: Matrix44) = Matrix44(-m.row0, -m.row1, -m.row2, -m.row3)

    // arithmetic operators (binary, matrix)
    static member (+) (l: Matrix44, r: Matrix44) = Matrix44(l.row0 + r.row0, l.row1 + r.row1, l.row2 + r.row2, l.row3 + r.row3)
    static member (-) (l: Matrix44, r: Matrix44) = Matrix44(l.row0 - r.row0, l.row1 - r.row1, l.row2 - r.row2, l.row3 - r.row3)

    // arithmetic operators (binary, scalar)
    static member (*) (l: float32, r: Matrix44) = Matrix44(l * r.row0, l * r.row1, l * r.row2, l * r.row3)
    static member (*) (l: Matrix44, r: float32) = Matrix44(l.row0 * r, l.row1 * r, l.row2 * r, l.row3 * r)
    static member (/) (l: Matrix44, r: float32) = Matrix44(l.row0 / r, l.row1 / r, l.row2 / r, l.row3 / r)

    // string representation
    override this.ToString() = sprintf "%A\n%A\n%A\n%A" this.row0 this.row1 this.row2 this.row3

    // constants
    static member Zero = Matrix44()
    static member Identity = Matrix44(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, Vector4.UnitW)