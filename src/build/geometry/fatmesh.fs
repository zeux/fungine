namespace Build.Geometry

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
type BoneInfluence =
    val mutable index: int
    val mutable weight: float32

[<Struct>]
type FatVertex =
    val mutable position: Vector3
    val mutable tangent: Vector3
    val mutable bitangent: Vector3
    val mutable normal: Vector3
    val mutable color: Color4 array
    val mutable texcoord: Vector2 array
    val mutable bones: BoneInfluence array

type FatMesh = { vertices: FatVertex array; indices: int array; skin: Render.SkinBinding option }