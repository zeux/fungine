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
        Vector3(this.row0.[index], this.row1.[index], this.row2.[index])

    member this.Item (row, column) = (this.Row row).[column]

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

    // matrix multiplication
    static member (*) (l: Matrix34, r: Matrix34) =
        Matrix34(l.row0.x * r.row0.x + l.row0.y * r.row1.x + l.row0.z * r.row2.x,
                 l.row0.x * r.row0.y + l.row0.y * r.row1.y + l.row0.z * r.row2.y,
                 l.row0.x * r.row0.z + l.row0.y * r.row1.z + l.row0.z * r.row2.z,
                 l.row0.x * r.row0.w + l.row0.y * r.row1.w + l.row0.z * r.row2.w + l.row0.w,

                 l.row1.x * r.row0.x + l.row1.y * r.row1.x + l.row1.z * r.row2.x,
                 l.row1.x * r.row0.y + l.row1.y * r.row1.y + l.row1.z * r.row2.y,
                 l.row1.x * r.row0.z + l.row1.y * r.row1.z + l.row1.z * r.row2.z,
                 l.row1.x * r.row0.w + l.row1.y * r.row1.w + l.row1.z * r.row2.w + l.row1.w,

                 l.row2.x * r.row0.x + l.row2.y * r.row1.x + l.row2.z * r.row2.x,
                 l.row2.x * r.row0.y + l.row2.y * r.row1.y + l.row2.z * r.row2.y,
                 l.row2.x * r.row0.z + l.row2.y * r.row1.z + l.row2.z * r.row2.z,
                 l.row2.x * r.row0.w + l.row2.y * r.row1.w + l.row2.z * r.row2.w + l.row2.w)

    // string representation
    override this.ToString() = sprintf "%A\n%A\n%A" this.row0 this.row1 this.row2

    // determinant
    member this.Determinant =
        this.row0.x * (this.row1.y * this.row2.z - this.row1.z * this.row2.y) -
        this.row0.y * (this.row1.x * this.row2.z - this.row1.z * this.row2.x) +
        this.row0.z * (this.row1.x * this.row2.y - this.row1.y * this.row2.x)

    // constants
    static member Zero = Matrix34()
    static member Identity = Matrix34(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ)

    // translation transformation
    static member Translation (x, y, z) =
        Matrix34(1.f, 0.f, 0.f, x,
                 0.f, 1.f, 0.f, y,
                 0.f, 0.f, 1.f, z)

    static member Translation (v: Vector3) = Matrix34.Translation(v.x, v.y, v.z)

    // axis-angle rotation transformation
    static member RotationAxis (axis, angle: float32) =
        let a = Vector3.Normalize(axis)
        let c = cos angle
        let s = sin angle
        let t = 1.f - c

        Matrix34(t * a.x * a.x + c,       t * a.x * a.y - a.z * s, t * a.x * a.z + a.y * s, 0.f,
                 t * a.x * a.y + a.z * s, t * a.y * a.y + c,       t * a.y * a.z - a.x * s, 0.f,
                 t * a.x * a.z - a.y * s, t * a.y * a.z + a.x * s, t * a.z * a.z + c,       0.f)

    // scaling transformation
    static member Scaling (x, y, z) =
        Matrix34(x,   0.f, 0.f, 0.f,
                 0.f, y,   0.f, 0.f,
                 0.f, 0.f, z,   0.f)

    static member Scaling (v: float32) = Matrix34.Scaling(v, v, v)
    static member Scaling (v: Vector3) = Matrix34.Scaling(v.x, v.y, v.z)

    // transpose
    static member Transpose (m: Matrix34) =
        Matrix34(m.row0.x, m.row1.x, m.row2.x, 0.f,
                 m.row0.y, m.row1.y, m.row2.y, 0.f,
                 m.row0.z, m.row1.z, m.row2.z, 0.f)

    // inverse for affine transform
    static member InverseAffine (m: Matrix34) =
        Matrix34(m.row0.x, m.row1.x, m.row2.x, -(m.row0.x * m.row0.w + m.row0.y * m.row1.w + m.row0.z * m.row2.w),
                 m.row0.y, m.row1.y, m.row2.y, -(m.row1.x * m.row0.w + m.row1.y * m.row1.w + m.row1.z * m.row2.w),
                 m.row0.z, m.row1.z, m.row2.z, -(m.row2.x * m.row0.w + m.row2.y * m.row1.w + m.row2.z * m.row2.w))

    // general inverse
    static member Inverse (m: Matrix34) =
        // get reciprocal determinant
        let s = 1.f / m.Determinant

        // get cofactors
        let r00 =  (m.row1.y * m.row2.z - m.row1.z * m.row2.y) * s
        let r01 = -(m.row0.y * m.row2.z - m.row0.z * m.row2.y) * s
        let r02 =  (m.row0.y * m.row1.z - m.row0.z * m.row1.y) * s

        let r10 = -(m.row1.x * m.row2.z - m.row1.z * m.row2.x) * s
        let r11 =  (m.row0.x * m.row2.z - m.row0.z * m.row2.x) * s
        let r12 = -(m.row0.x * m.row1.z - m.row0.z * m.row1.x) * s

        let r20 =  (m.row1.x * m.row2.y - m.row1.y * m.row2.x) * s
        let r21 = -(m.row0.x * m.row2.y - m.row0.y * m.row2.x) * s
        let r22 =  (m.row0.x * m.row1.y - m.row0.y * m.row1.x) * s

        // get final matrix
        Matrix34(r00, r01, r02, -(r00 * m.row0.w + r01 * m.row1.w + r02 * m.row2.w),
                 r10, r11, r12, -(r10 * m.row0.w + r11 * m.row1.w + r12 * m.row2.w),
                 r20, r21, r22, -(r20 * m.row0.w + r21 * m.row1.w + r22 * m.row2.w))

    // transform vector as a position
    static member TransformPosition (m: Matrix34, v: Vector3) =
        Vector3(v.x * m.row0.x + v.y * m.row0.y + v.z * m.row0.z + m.row0.w,
                v.x * m.row1.x + v.y * m.row1.y + v.z * m.row1.z + m.row1.w,
                v.x * m.row2.x + v.y * m.row2.y + v.z * m.row2.z + m.row2.w)

    // transform vector as a direction
    static member TransformDirection (m: Matrix34, v: Vector3) =
        Vector3(v.x * m.row0.x + v.y * m.row0.y + v.z * m.row0.z,
                v.x * m.row1.x + v.y * m.row1.y + v.z * m.row1.z,
                v.x * m.row2.x + v.y * m.row2.y + v.z * m.row2.z)