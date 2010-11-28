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

type FatMesh() =
    member x.Foo = 1