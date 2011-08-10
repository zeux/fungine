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
        Vector4(this.row0.[index], this.row1.[index], this.row2.[index], this.row3.[index])

    member this.Item (row, column) = (this.Row row).[column]

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

    // matrix multiplication
    static member (*) (l: Matrix44, r: Matrix44) =
        Matrix44(l.row0.x * r.row0.x + l.row0.y * r.row1.x + l.row0.z * r.row2.x + l.row0.w * r.row3.x,
                 l.row0.x * r.row0.y + l.row0.y * r.row1.y + l.row0.z * r.row2.y + l.row0.w * r.row3.y,
                 l.row0.x * r.row0.z + l.row0.y * r.row1.z + l.row0.z * r.row2.z + l.row0.w * r.row3.z,
                 l.row0.x * r.row0.w + l.row0.y * r.row1.w + l.row0.z * r.row2.w + l.row0.w * r.row3.w,

                 l.row1.x * r.row0.x + l.row1.y * r.row1.x + l.row1.z * r.row2.x + l.row1.w * r.row3.x,
                 l.row1.x * r.row0.y + l.row1.y * r.row1.y + l.row1.z * r.row2.y + l.row1.w * r.row3.y,
                 l.row1.x * r.row0.z + l.row1.y * r.row1.z + l.row1.z * r.row2.z + l.row1.w * r.row3.z,
                 l.row1.x * r.row0.w + l.row1.y * r.row1.w + l.row1.z * r.row2.w + l.row1.w * r.row3.w,

                 l.row2.x * r.row0.x + l.row2.y * r.row1.x + l.row2.z * r.row2.x + l.row2.w * r.row3.x,
                 l.row2.x * r.row0.y + l.row2.y * r.row1.y + l.row2.z * r.row2.y + l.row2.w * r.row3.y,
                 l.row2.x * r.row0.z + l.row2.y * r.row1.z + l.row2.z * r.row2.z + l.row2.w * r.row3.z,
                 l.row2.x * r.row0.w + l.row2.y * r.row1.w + l.row2.z * r.row2.w + l.row2.w * r.row3.w,

                 l.row3.x * r.row0.x + l.row3.y * r.row1.x + l.row3.z * r.row2.x + l.row3.w * r.row3.x,
                 l.row3.x * r.row0.y + l.row3.y * r.row1.y + l.row3.z * r.row2.y + l.row3.w * r.row3.y,
                 l.row3.x * r.row0.z + l.row3.y * r.row1.z + l.row3.z * r.row2.z + l.row3.w * r.row3.z,
                 l.row3.x * r.row0.w + l.row3.y * r.row1.w + l.row3.z * r.row2.w + l.row3.w * r.row3.w)

    // string representation
    override this.ToString() = sprintf "%A\n%A\n%A\n%A" this.row0 this.row1 this.row2 this.row3

    // determinant
    member this.Determinant =
        // get determinants of 2x2 submatrices
        let a0 = this.row0.x * this.row1.y - this.row0.y * this.row1.x
        let a1 = this.row0.x * this.row1.z - this.row0.z * this.row1.x
        let a2 = this.row0.x * this.row1.w - this.row0.w * this.row1.x
        let a3 = this.row0.y * this.row1.z - this.row0.z * this.row1.y
        let a4 = this.row0.y * this.row1.w - this.row0.w * this.row1.y
        let a5 = this.row0.z * this.row1.w - this.row0.w * this.row1.z
        let b0 = this.row2.x * this.row3.y - this.row2.y * this.row3.x
        let b1 = this.row2.x * this.row3.z - this.row2.z * this.row3.x
        let b2 = this.row2.x * this.row3.w - this.row2.w * this.row3.x
        let b3 = this.row2.y * this.row3.z - this.row2.z * this.row3.y
        let b4 = this.row2.y * this.row3.w - this.row2.w * this.row3.y
        let b5 = this.row2.z * this.row3.w - this.row2.w * this.row3.z

        a0 * b5 - a1 * b4 + a2 * b3 + a3 * b2 - a4 * b1 + a5 * b0

    // constants
    static member Zero = Matrix44()
    static member Identity = Matrix44(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, Vector4.UnitW)

    // transpose
    static member Transpose (m: Matrix44) =
        Matrix44(m.row0.x, m.row1.x, m.row2.x, m.row3.x,
                 m.row0.y, m.row1.y, m.row2.y, m.row3.y,
                 m.row0.z, m.row1.z, m.row2.z, m.row3.z,
                 m.row0.w, m.row1.w, m.row2.w, m.row3.w)

    // general inverse
    static member Inverse (m: Matrix44) =
        // get determinants of 2x2 submatrices
        let a0 = m.row0.x * m.row1.y - m.row0.y * m.row1.x
        let a1 = m.row0.x * m.row1.z - m.row0.z * m.row1.x
        let a2 = m.row0.x * m.row1.w - m.row0.w * m.row1.x
        let a3 = m.row0.y * m.row1.z - m.row0.z * m.row1.y
        let a4 = m.row0.y * m.row1.w - m.row0.w * m.row1.y
        let a5 = m.row0.z * m.row1.w - m.row0.w * m.row1.z
        let b0 = m.row2.x * m.row3.y - m.row2.y * m.row3.x
        let b1 = m.row2.x * m.row3.z - m.row2.z * m.row3.x
        let b2 = m.row2.x * m.row3.w - m.row2.w * m.row3.x
        let b3 = m.row2.y * m.row3.z - m.row2.z * m.row3.y
        let b4 = m.row2.y * m.row3.w - m.row2.w * m.row3.y
        let b5 = m.row2.z * m.row3.w - m.row2.w * m.row3.z

        // get reciprocal determinant
        let det = a0 * b5 - a1 * b4 + a2 * b3 + a3 * b2 - a4 * b1 + a5 * b0
        let s = 1.f / det
        
        // get final matrix
        Matrix44(
            (+ m.row1.y * b5 - m.row1.z * b4 + m.row1.w * b3) * s,
            (- m.row0.y * b5 + m.row0.z * b4 - m.row0.w * b3) * s,
            (+ m.row3.y * a5 - m.row3.z * a4 + m.row3.w * a3) * s,
            (- m.row2.y * a5 + m.row2.z * a4 - m.row2.w * a3) * s,

            (- m.row1.x * b5 + m.row1.z * b2 - m.row1.w * b1) * s,
            (+ m.row0.x * b5 - m.row0.z * b2 + m.row0.w * b1) * s,
            (- m.row3.x * a5 + m.row3.z * a2 - m.row3.w * a1) * s,
            (+ m.row2.x * a5 - m.row2.z * a2 + m.row2.w * a1) * s,

            (+ m.row1.x * b4 - m.row1.y * b2 + m.row1.w * b0) * s,
            (- m.row0.x * b4 + m.row0.y * b2 - m.row0.w * b0) * s,
            (+ m.row3.x * a4 - m.row3.y * a2 + m.row3.w * a0) * s,
            (- m.row2.x * a4 + m.row2.y * a2 - m.row2.w * a0) * s,

            (- m.row1.x * b3 + m.row1.y * b1 - m.row1.z * b0) * s,
            (+ m.row0.x * b3 - m.row0.y * b1 + m.row0.z * b0) * s,
            (- m.row3.x * a3 + m.row3.y * a1 - m.row3.z * a0) * s,
            (+ m.row2.x * a3 - m.row2.y * a1 + m.row2.z * a0) * s)