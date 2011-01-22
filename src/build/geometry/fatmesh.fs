namespace Build.Geometry

// a single format component for the fat vertex
type FatVertexComponent =
    | Position
    | Tangent
    | Bitangent
    | Normal
    | Color of int
    | TexCoord of int
    | SkinningInfo of int

// fat vertex components
type FatVertexFormat = FatVertexComponent array

// single bone vertex influence
[<Struct>]
type BoneInfluence =
    val mutable index: int
    val mutable weight: float32

// fat vertex, which consists of all components for a single vertex
[<Struct>]
type FatVertex =
    val mutable position: Vector3
    val mutable tangent: Vector3
    val mutable bitangent: Vector3
    val mutable normal: Vector3
    val mutable color: Color4 array
    val mutable texcoord: Vector2 array
    val mutable bones: BoneInfluence array

// fat mesh (non-indexed triangle data and an optional skinning data)
type FatMesh = { vertices: FatVertex array; skin: Render.SkinBinding option }