namespace Render

// skin binding data; consists of a subset of skeleton bones and inverse bind pose matrices
type SkinBinding(bones: int array, invBindPose: Matrix34 array) =
    do assert (bones.Length = invBindPose.Length)

    // get binding data
    member this.Bones = bones
    member this.InvBindPose = invBindPose

    // compute bone-space transforms from the skeleton
    member this.ComputeBoneTransforms (skeleton: SkeletonInstance) =
        Array.map2 (fun bone invBind -> (skeleton.AbsoluteTransform bone) * invBind) bones invBindPose

// quantization coefficients for compressed vertex data
[<ShaderStruct>]
type MeshCompressionInfo =
    { posOffset: Vector3
      posScale: Vector3
      uvOffset: Vector2
      uvScale: Vector2 }

// mesh bound information
[<Struct>]
type MeshBoundsInfo(bone: int, localBounds: Math.AABB) =
    member this.Bone = bone
    member this.LocalBounds = localBounds

// mesh fragment; represents one draw call
type MeshFragment =
    { material: Material
      skin: SkinBinding
      compressionInfo: MeshCompressionInfo
      vertexFormat: Render.VertexFormat
      indexFormat: Render.Format
      vertexOffset: int
      indexOffset: int
      indexCount: int
      bounds: MeshBoundsInfo array
    }

// mesh
type Mesh =
    { fragments: MeshFragment array
      vertices: VertexBuffer
      indices: IndexBuffer
      skeleton: SkeletonInstance
      bounds: MeshBoundsInfo array
    }

// mesh instance
type MeshInstance =
    { proto: Mesh
      skeleton: SkeletonInstance
    }