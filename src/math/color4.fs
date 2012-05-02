namespace Math

open System.Runtime.InteropServices

#nowarn "9" // Uses of this construct may result in the generation of unverifiable .NET IL code

[<Struct; StructLayout(LayoutKind.Sequential, Pack = 4)>]
type Color4 =
    val r: float32
    val g: float32
    val b: float32
    val a: float32

    new (r, g, b, a) = { r = r; g = g; b = b; a = a }
    new (r, g, b) = { r = r; g = g; b = b; a = 1.f }