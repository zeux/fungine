namespace Math

open System.Runtime.InteropServices

#nowarn "9" // Uses of this construct may result in the generation of unverifiable .NET IL code

[<Struct; StructLayout(LayoutKind.Sequential, Pack = 4)>]
type Vector4 =
    val x: float32
    val y: float32
    val z: float32
    val w: float32

    new (x, y, z, w) = { x = x; y = y; z = z; w = w }