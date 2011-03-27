namespace Render

// skin binding data; consists of a subset of skeleton bones and inverse bind pose matrices
type SkinBinding(bones: int array, inv_bind_pose: Matrix34 array) =
    do assert (bones.Length = inv_bind_pose.Length)

    // compute bone-space transforms from the skeleton
    member this.ComputeBoneTransforms (skeleton: SkeletonInstance) =
        Array.map2 (fun bone inv_bind -> (skeleton.AbsoluteTransform bone) * inv_bind) bones inv_bind_pose

// quantization coefficients for compressed vertex data
type MeshCompressionInfo =
    { pos_offset: Vector3
      pos_scale: Vector3
      uv_offset: Vector2
      uv_scale: Vector2 }

// mesh fragment; represents one draw call
type MeshFragment =
    { material: Material
      skin: SkinBinding
      compression_info: MeshCompressionInfo
      vertex_format: Render.VertexFormat
      index_format: Render.Format
      vertex_offset: int
      index_offset: int
      index_count: int
    }

// mesh
type Mesh =
    { fragments: MeshFragment array
      vertices: Buffer
      indices: Buffer
      skeleton: SkeletonInstance
    }

// mesh instance
type MeshInstance =
    { proto: Mesh
      skeleton: SkeletonInstance
    }