namespace Build.Geometry

open System.Collections.Generic

type PackedMeshCompressionInfo =
    { position_offset: Vector3
      position_scale: Vector3
      texcoord_offset: Vector2
      texcoord_scale: Vector2 }

type PackedMesh =
    { compression_info: PackedMeshCompressionInfo
      format: Render.VertexFormat
      vertices: byte array
      indices: int array
      skin: Render.SkinBinding option }

module MeshPacker =
    let private project v n =
        Vector3.Dot(v, n) * n

    let private orthonormalize e0 e1 e2 =
        let r0 = Vector3.Normalize(e0)
        let r1 = Vector3.Normalize(e1 - project e1 r0)
        let r2 = Vector3.Normalize(e2 - project e2 r0 - project e2 r1)
        r0, r1, r2

    let inline private getComponentBounds vertices access minimize maximize =
        let data = vertices |> Array.map access
        let data_min = data |> Array.reduce (fun a b -> minimize(a, b))
        let data_max = data |> Array.reduce (fun a b -> maximize(a, b))
        data_min, data_max - data_min

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

    let private packVertices (vertices: Build.Geometry.FatVertex array) (format: Render.VertexFormat) =
        // only a restricted set of formats supported atm
        let supported_formats = [|Render.VertexFormats.Pos_TBN_Tex1_Bone4_Packed|]
        if not (Array.exists (fun f -> format = f) supported_formats) then failwith "Unsupported format"

        // prepare a stream for writing
        let result : byte array = Array.zeroCreate (vertices.Length * format.size)
        use stream = new SlimDX.DataStream(result, canRead = false, canWrite = true)

        // get position and texcoord bounds for compression
        let (position_offset, position_scale) = getComponentBounds vertices (fun v -> v.position) Vector3.Minimize Vector3.Maximize
        let (texcoord_offset, texcoord_scale) = getComponentBounds vertices (fun v -> if v.texcoord <> null then v.texcoord.[0] else Vector2()) Vector2.Minimize Vector2.Maximize

        // pack the data
        for v in vertices do
            // position: 3 unorm16 + padding
            stream.Write(uint16 (Math.Pack.packFloatUNorm ((v.position.X - position_offset.X) / position_scale.X) 16))
            stream.Write(uint16 (Math.Pack.packFloatUNorm ((v.position.Y - position_offset.Y) / position_scale.Y) 16))
            stream.Write(uint16 (Math.Pack.packFloatUNorm ((v.position.Z - position_offset.Z) / position_scale.Z) 16))
            stream.Write(uint16 0)

            // TBN: assume orthonormal basis, normal is 3 unorm8 + padding, tangent is 3 unorm8 + bitangent sign
            let normal, tangent, bitangent = orthonormalize v.normal v.tangent v.bitangent
            let bitangent_sign = sign (Vector3.Dot(Vector3.Cross(normal, tangent), bitangent))
            
            stream.Write(Math.Pack.packDirectionR8G8B8(normal))
            stream.Write(Math.Pack.packDirectionR8G8B8(tangent) ||| ((Math.Pack.packFloatSNorm (float32 bitangent_sign) 8) <<< 24))

            // texcoord: 2 unorm16
            let uv0 = if v.texcoord <> null then v.texcoord.[0] else Vector2()

            stream.Write(uint16 (Math.Pack.packFloatUNorm ((uv0.X - texcoord_offset.X) / texcoord_scale.X) 16))
            stream.Write(uint16 (Math.Pack.packFloatUNorm ((uv0.Y - texcoord_offset.Y) / texcoord_scale.Y) 16))

            // bone data: 4 uint8 indices, 4 unorm8 weights, weights are normalized to give sum of 255
            let (bone_indices, bone_weights) = if v.bones <> null then packBoneInfluences v.bones else [|0uy; 0uy; 0uy; 0uy|], [|255uy; 0uy; 0uy; 0uy|]

            stream.WriteRange(bone_indices)
            stream.WriteRange(bone_weights)

        // get the vertex and compression data
        let compression_info = { new PackedMeshCompressionInfo with position_offset = position_offset and position_scale = position_scale and texcoord_offset = texcoord_offset and texcoord_scale = texcoord_scale }
        
        result, compression_info

    let pack (mesh: Build.Geometry.FatMesh) format =
        // build vertex data
        let vertices, compression_info = packVertices mesh.vertices format

        // build index data
        let remap = Dictionary<byte array, int>(HashIdentity.Structural)

        let indices = Array.init mesh.vertices.Length (fun i ->
            let vertex = Array.sub vertices (i * format.size) format.size

            match remap.TryGetValue(vertex) with
            | true, index -> index
            | false, _ ->
                let index = remap.Count
                remap.Add(vertex, index)
                index)

        // build indexed vertex data
        let indexed_vertices = Array.zeroCreate (remap.Count * format.size)

        for kvp in remap do
            let vertex = kvp.Key
            let index = kvp.Value

            Array.blit vertex 0 indexed_vertices (index * format.size) format.size

        // build the mesh
        { new PackedMesh with compression_info = compression_info and format = format and vertices = indexed_vertices and indices = indices and skin = mesh.skin }