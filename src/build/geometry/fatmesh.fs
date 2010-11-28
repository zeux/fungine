namespace Build.Geometry

open SlimDX

type FatVertexComponent =
    | Position
    | Tangent
    | Bitangent
    | Normal
    | Color of int
    | TexCoord of int
    | SkinningInfo of int

type FatVertexFormat = FatVertexComponent array

[<Struct>]
type FatVertex =
    val mutable position: Vector3
    val mutable tangent: Vector3
    val mutable bitangent: Vector3
    val mutable normal: Vector3
    val mutable color: Color4 array
    val mutable texcoord: Vector2 array

type FatMesh = { vertices: FatVertex array; indices: int array }