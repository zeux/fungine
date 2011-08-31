namespace Build.Geometry

open System.Collections.Generic

// an indexed mesh with compressed vertex data
type PackedMesh =
    { compression_info: Render.MeshCompressionInfo
      format: Render.VertexFormat
      vertex_size: int
      vertices: byte array
      indices: int array
      skin: Render.SkinBinding option }

module MeshPacker =
    // project vector on a normal
    let private project v n =
        Vector3.Dot(v, n) * n

    // orthonormalize three basis vectors using Gram-Schmidt process (in the e0-e1-e2 order)
    let private orthonormalize e0 e1 e2 =
        let r0 = Vector3.Normalize(e0)
        let r1 = Vector3.Normalize(e1 - project e1 r0)
        let r2 = Vector3.Normalize(e2 - project e2 r0 - project e2 r1)
        r0, r1, r2

    // get per-comonent minimum and maximum for vertex data
    let inline private getComponentBounds vertices access minimize maximize =
        let data = vertices |> Array.map access
        let data_min = data |> Array.reduce (fun a b -> minimize(a, b))
        let data_max = data |> Array.reduce (fun a b -> maximize(a, b))
        data_min, data_max - data_min

    // pack an array of bone influences to two byte4 vectors
    let private packBoneInfluences (bones: Build.Geometry.BoneInfluence array) =
        let indices = Array.zeroCreate 4
        let weights = Array.zeroCreate 4

        // pack indices and weights to bytes
        bones |> Array.iteri (fun index bone ->
            assert (bone.index < 256)
            indices.[index] <- byte bone.index
            weights.[index] <- byte (Math.Pack.packFloatUNorm bone.weight 8))

        // correct the first (largest, since weights are sorted by mesh builder) weight, so that the sum is exactly 255
        let weight_sum = Array.sumBy (fun i -> int i) (Array.sub weights 1 (weights.Length - 1))
        assert (weight_sum <= 255)
        weights.[0] <- byte (255 - weight_sum)

        indices, weights

    // pack uncompressed vertex data using the desired format
    let private packVertices (vertices: Build.Geometry.FatVertex array) (format: Render.VertexFormat) vertex_size =
        // only a restricted set of formats supported atm
        let supported_formats = [|Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed|]
        if not (Array.exists (fun f -> format = f) supported_formats) then failwithf "Unsupported format %A" format

        // prepare a stream for writing
        let result : byte array = Array.zeroCreate (vertices.Length * vertex_size)
        use stream = new SlimDX.DataStream(result, canRead = false, canWrite = true)

        // get position and texcoord bounds for compression
        let (pos_offset, pos_scale) = getComponentBounds vertices (fun v -> v.position) Vector3.Minimize Vector3.Maximize
        let (uv_offset, uv_scale) = getComponentBounds vertices (fun v -> if v.texcoord <> null then v.texcoord.[0] else Vector2()) Vector2.Minimize Vector2.Maximize

        // pack the data
        for v in vertices do
            let rescale value offset scale = if scale = 0.f then 0.f else (value - offset) / scale

            // position: 3 unorm16 + padding
            stream.Write(uint16 (Math.Pack.packFloatUNorm (rescale v.position.x pos_offset.x pos_scale.x) 16))
            stream.Write(uint16 (Math.Pack.packFloatUNorm (rescale v.position.y pos_offset.y pos_scale.y) 16))
            stream.Write(uint16 (Math.Pack.packFloatUNorm (rescale v.position.z pos_offset.z pos_scale.z) 16))
            stream.Write(uint16 0)

            // TBN: assume orthonormal basis, normal is 3 unorm8 + padding, tangent is 3 unorm8 + bitangent sign
            let normal, tangent, bitangent = orthonormalize v.normal v.tangent v.bitangent
            let bitangent_sign = sign (Vector3.Dot(Vector3.Cross(normal, tangent), bitangent))
            
            stream.Write(Math.Pack.packDirectionUNorm normal 10)
            stream.Write((Math.Pack.packDirectionUNorm tangent 10) ||| ((if bitangent_sign = 1 then 3u else 0u) <<< 30))

            // texcoord: 2 unorm16
            let uv0 = if v.texcoord <> null then v.texcoord.[0] else Vector2()

            stream.Write(uint16 (Math.Pack.packFloatUNorm (rescale uv0.x uv_offset.x uv_scale.x) 16))
            stream.Write(uint16 (Math.Pack.packFloatUNorm (rescale uv0.y uv_offset.y uv_scale.y) 16))

            // bone data: 4 uint8 indices, 4 unorm8 weights, weights are normalized to give sum of 255
            let (bone_indices, bone_weights) = if v.bones <> null then packBoneInfluences v.bones else [|0uy; 0uy; 0uy; 0uy|], [|255uy; 0uy; 0uy; 0uy|]

            stream.WriteRange(bone_indices)
            stream.WriteRange(bone_weights)

        // get the vertex and compression data
        let compression_info = { new Render.MeshCompressionInfo with pos_offset = pos_offset and pos_scale = pos_scale and uv_offset = uv_offset and uv_scale = uv_scale }
        
        result, compression_info

    // pack fat mesh using the desired format for vertices, merge equal vertices (results in vertex/index buffer pair)
    let pack (mesh: Build.Geometry.FatMesh) format =
        // get vertex size from the format
        let layout = Render.VertexLayouts.get format
        let vertex_size = layout.size

        // build vertex data
        let vertices, compression_info = packVertices mesh.vertices format vertex_size

        // build index data
        let remap = Dictionary<byte array, int>(HashIdentity.Structural)

        let indices = Array.init mesh.vertices.Length (fun i ->
            let vertex = Array.sub vertices (i * vertex_size) vertex_size

            Core.CacheUtil.update remap vertex (fun _ -> remap.Count))

        // build indexed vertex data
        let indexed_vertices = Array.zeroCreate (remap.Count * vertex_size)

        for kvp in remap do
            let vertex = kvp.Key
            let index = kvp.Value

            Array.blit vertex 0 indexed_vertices (index * vertex_size) vertex_size

        // build the mesh
        { new PackedMesh with compression_info = compression_info and format = format and vertex_size = vertex_size and vertices = indexed_vertices and indices = indices and skin = mesh.skin }
