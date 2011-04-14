module Build.Dae.MeshBuilder

open System.Xml

open Build.Geometry
open Build.Dae.Parse

// convert fat mesh to packed & optimized mesh
let private buildOptimizedMesh fat_mesh format =
    // build packed & indexed mesh
    let packed_mesh = MeshPacker.pack fat_mesh format

    // optimize for Post T&L cache
    let postopt_mesh = { packed_mesh with indices = PostTLOptimizerLinear.optimize packed_mesh.indices }

    // optimize for Pre T&L cache
    let (vertices, indices) = PreTLOptimizer.optimize postopt_mesh.vertices postopt_mesh.indices postopt_mesh.vertex_size
    { postopt_mesh with vertices = vertices; indices = indices }

// get a byte copy of the array
let private getByteCopy (arr: 'a array) =
    let result: byte array = Array.zeroCreate (arr.Length * sizeof<'a>)
    System.Buffer.BlockCopy(arr, 0, result, 0, result.Length)
    result

// pack index buffer to 16/32 bits
let private packIndexBuffer indices vertex_count =
    // pack to the smallest available size
    if vertex_count <= 65536 then
        Render.Format.R16_UInt, indices |> Array.map (uint16) |> getByteCopy
    else
        Render.Format.R32_UInt, indices |> getByteCopy

// merge several arrays into one
let private mergeArrays (arrays: byte array array) =
    // get merged array
    let result = Array.concat arrays

    // get subarray offsets
    let offsets = arrays |> Array.map (fun arr -> arr.Length) |> Array.scan (+) 0

    result, Array.sub offsets 0 (offsets.Length - 1)

// merge several meshes into a single vertex/buffer pair
let private mergeMeshGeometry (meshes: PackedMesh array) =
    // get vertex data
    let vertices = meshes |> Array.map (fun mesh -> mesh.vertices)

    // get index data
    let indices = meshes |> Array.map (fun mesh -> packIndexBuffer mesh.indices (mesh.vertices.Length / mesh.vertex_size))
    let (index_formats, index_data) = Array.unzip indices

    // merge arrays
    let merged_vertices, vertex_offsets = mergeArrays vertices
    let merged_indices, index_offsets = mergeArrays index_data

    // gather mesh data
    let mesh_data = Array.zip3 vertex_offsets index_offsets index_formats

    merged_vertices, merged_indices, mesh_data

// build packed & optimized meshes from document
let private buildPackedMeshes (doc: Document) conv skeleton =
    // use a constant FVF for now
    let fvf = [|Position; Tangent; Bitangent; Normal; TexCoord 0; SkinningInfo 4|]
    let format = Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed

    // get all instance nodes
    let instances = doc.Root.Select("/COLLADA/library_visual_scenes//node/instance_geometry | /COLLADA/library_visual_scenes//node/instance_controller")

    // get all meshes
    let fat_meshes = instances |> Array.collect (fun i ->
        FatMeshBuilder.build doc conv i fvf skeleton
        |> Array.map (fun (mesh, material) -> i, mesh, material))

    // build packed & optimized meshes
    fat_meshes |> Array.map (fun (inst, mesh, material) -> inst, buildOptimizedMesh mesh format, material)

// build mesh fragments
let private buildMeshFragments meshes mesh_data materials skeleton =
    let dummy_inv_bind_pose = [|Matrix34.Identity|]

    meshes |> Array.mapi (fun idx (inst: XmlNode, mesh, _) ->
        let (vertex_offset, index_offset, index_format) = Array.get mesh_data idx

        // create dummy 1-bone skin binding for non-skinned meshes
        let skin =
            match mesh.skin with
            | Some binding -> binding
            | None -> Render.SkinBinding([| skeleton.node_map.[inst.ParentNode] |], dummy_inv_bind_pose)
        
        // build fragment structure
        { new Render.MeshFragment
          with material = Array.get materials idx
          and skin = skin
          and compression_info = mesh.compression_info
          and vertex_format = mesh.format
          and index_format = index_format
          and vertex_offset = vertex_offset
          and index_offset = index_offset
          and index_count = mesh.indices.Length })

// build mesh file from dae file
let build source target =
    // parse .dae file
    let doc = Document(source)

    // get cached texture/material builders
    let all_textures = Core.Cache (fun id -> TextureBuilder.build doc id)
    let all_materials = Core.Cache (fun id -> MaterialBuilder.build doc id (all_textures.Get >> snd))

    // get basis converter (convert up axis, skip unit conversion)
    let conv = BasisConverter(doc)

    // export skeleton
    let skeleton = SkeletonBuilder.build doc conv

    // build packed & optimized meshes
    let meshes = buildPackedMeshes doc conv skeleton

    // build merged vertex & index buffers
    let (vertices, indices, mesh_data) = mergeMeshGeometry (meshes |> Array.map (fun (_, mesh, _) -> mesh))
    let vertex_buffer = Render.Buffer(SlimDX.Direct3D11.BindFlags.VertexBuffer, vertices)
    let index_buffer = Render.Buffer(SlimDX.Direct3D11.BindFlags.IndexBuffer, indices)

    // build materials
    let materials = meshes |> Array.map (fun (_, _, material) -> all_materials.Get material)

    // build mesh fragments
    let fragments = buildMeshFragments meshes mesh_data materials skeleton

    // build & save mesh
    let mesh = { new Render.Mesh with fragments = fragments and vertices = vertex_buffer and indices = index_buffer and skeleton = skeleton.data }

    Core.Serialization.Save.toFile target mesh

    // return texture list
    all_textures.Pairs |> Seq.map (fun p -> p.Value)