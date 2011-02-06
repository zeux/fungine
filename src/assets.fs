module assets

open System.Collections.Generic

open Build.Geometry
open Build.Dae
open Build.Dae.Parse

let build source target func =
    let source_info = System.IO.FileInfo(source)
    let target_info = System.IO.FileInfo(target)

    if not target_info.Exists || source_info.LastWriteTime > target_info.LastWriteTime then
        System.IO.Directory.CreateDirectory(target_info.DirectoryName) |> ignore

        let result = func source target
        if not result then failwithf "Error building asset %s" target

let changeExtension name ext =
    System.IO.Path.ChangeExtension(name, ext)

let buildTexture source =
    build source (".build/" + changeExtension source ".dds") Build.Texture.build

let buildMeshImpl source target =
    // export .dae file
    let dae = ".build/" + changeExtension source ".dae"
    build source dae Export.build

    // parse .dae file
    let doc = Document(dae)

    // get cached texture/material builders
    let all_textures = Core.Cache (fun id -> Build.Dae.TextureBuilder.build doc id)
    let all_materials = Core.Cache (fun id -> Build.Dae.MaterialBuilder.build doc id all_textures.Get)

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
        let packed_meshes = fat_meshes |> Array.map (fun (mesh, _) -> Build.Geometry.MeshPacker.pack mesh format)

        // optimize for Post T&L cache
        let postopt_meshes = packed_meshes |> Array.map (fun mesh -> { mesh with indices = Build.Geometry.PostTLOptimizerLinear.optimize mesh.indices })

        // optimize for Pre T&L cache
        let preopt_meshes = postopt_meshes |> Array.map (fun mesh ->
            let (vertices, indices) = Build.Geometry.PreTLOptimizer.optimize mesh.vertices mesh.indices mesh.vertex_size
            { mesh with vertices = vertices; indices = indices })

        Array.map2 (fun mesh material -> mesh, material, skeleton.data, skeleton.data.AbsoluteTransform skeleton.node_map.[i.ParentNode]) preopt_meshes materials)

    Core.Serialization.Save.toFile target meshes

    // export textures
    all_textures.Values |> Seq.iter (fun tex -> buildTexture tex.Path)

    true

let buildMesh source =
    let target = ".build/" + changeExtension source ".mesh"

    build source target buildMeshImpl

    (Core.Serialization.Load.fromFile target) :?> (PackedMesh * Render.Material * Render.Skeleton * Matrix34) array
    
let buildMeshes path =
    let patterns = [|"*.mb"; "*.ma"; "*.max"|]
    let files = patterns |> Array.collect (fun p -> System.IO.Directory.GetFiles(path, p, System.IO.SearchOption.AllDirectories))
    files |> Array.collect buildMesh

let buildAll () =
    buildMeshes "art"
