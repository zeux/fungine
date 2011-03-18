module Build.Dae.MeshBuilder

open Build.Geometry
open Build.Dae.Parse

let build source target =
    // parse .dae file
    let doc = Document(source)

    // get cached texture/material builders
    let all_textures = Core.Cache (fun id -> TextureBuilder.build doc id)
    let all_materials = Core.Cache (fun id -> MaterialBuilder.build doc id (all_textures.Get >> snd))

    // get basis converter (converts up axis and units)
    let conv = BasisConverter(doc, 1.f)

    // export skeleton
    let skeleton = SkeletonBuilder.build doc conv

    // use a constant FVF for now
    let fvf = [|Position; Tangent; Bitangent; Normal; TexCoord 0; SkinningInfo 4|]
    let format = Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed

    // export meshes
    let instances = doc.Root.Select("/COLLADA/library_visual_scenes//node/instance_geometry | /COLLADA/library_visual_scenes//node/instance_controller")

    let meshes = instances |> Array.collect (fun i ->
        // build fat meshes from .dae
        let fat_meshes = FatMeshBuilder.build doc conv i fvf skeleton

        // build materials
        let materials = fat_meshes |> Array.map (fun (_, material) -> all_materials.Get material)

        // build packed & indexed meshes
        let packed_meshes = fat_meshes |> Array.map (fun (mesh, _) -> MeshPacker.pack mesh format)

        // optimize for Post T&L cache
        let postopt_meshes = packed_meshes |> Array.map (fun mesh -> { mesh with indices = PostTLOptimizerLinear.optimize mesh.indices })

        // optimize for Pre T&L cache
        let preopt_meshes = postopt_meshes |> Array.map (fun mesh ->
            let (vertices, indices) = PreTLOptimizer.optimize mesh.vertices mesh.indices mesh.vertex_size
            { mesh with vertices = vertices; indices = indices })

        Array.map2 (fun mesh material -> mesh, material, skeleton.data, skeleton.data.AbsoluteTransform skeleton.node_map.[i.ParentNode]) preopt_meshes materials)

    Core.Serialization.Save.toFile target meshes

    // return texture list
    all_textures.Pairs |> Seq.map (fun p -> p.Value)