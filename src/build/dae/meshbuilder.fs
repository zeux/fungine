module Build.Dae.MeshBuilder

open System.Xml

open Build.Geometry
open Build.Dae.Parse

open BuildSystem

// convert fat mesh to packed & optimized mesh
let private buildOptimizedMesh fatMesh format =
    // build packed & indexed mesh
    let packedMesh, vertexRemap = MeshPacker.pack fatMesh format

    // optimize for Post T&L cache
    let postoptMesh = { packedMesh with indices = PostTLOptimizerTipsify.optimize packedMesh.indices 16 }

    // optimize for Pre T&L cache
    let (vertices, indices) = PreTLOptimizer.optimize postoptMesh.vertices postoptMesh.indices postoptMesh.vertexSize
    { postoptMesh with vertices = vertices; indices = indices }

// get a byte copy of the array
let private getByteCopy (arr: 'a array) =
    let result: byte array = Array.zeroCreate (arr.Length * sizeof<'a>)
    System.Buffer.BlockCopy(arr, 0, result, 0, result.Length)
    result

// pack index buffer to 16/32 bits
let private packIndexBuffer indices vertexCount =
    // pack to the smallest available size
    if vertexCount <= 65536 then
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
    let indices = meshes |> Array.map (fun mesh -> packIndexBuffer mesh.indices (mesh.vertices.Length / mesh.vertexSize))
    let (indexFormats, indexData) = Array.unzip indices

    // merge arrays
    let mergedVertices, vertexOffsets = mergeArrays vertices
    let mergedIndices, indexOffsets = mergeArrays indexData

    // gather mesh data
    let meshData = Array.zip3 vertexOffsets indexOffsets indexFormats

    mergedVertices, mergedIndices, meshData

// build bounding box information for a mesh
let private buildMeshBounds (mesh: FatMesh) =
    let update (box: Math.AABB) v = Math.AABB(Vector3.Minimize(box.Min, v), Vector3.Maximize(box.Max, v))
    let finish (box: Math.AABB) = if box.Min.x > box.Max.x then Math.AABB() else box
    let dummyBox = let v = Vector3(infinityf, infinityf, infinityf) in Math.AABB(v, -v)

    let mutable box = dummyBox
    let bones, invBindPose =
        match mesh.skin with
        | Some sb -> sb.Bones |> Array.map (fun b -> Render.MeshBoundsInfo(b, dummyBox)), sb.InvBindPose
        | _ -> [||], [||]

    for v in mesh.vertices do
        box <- update box v.position

        if v.bones <> null then
            for bi in v.bones do
                let bone = bones.[bi.index]
                bones.[bi.index] <- Render.MeshBoundsInfo(bone.Bone, update bone.LocalBounds $ Matrix34.TransformPosition(invBindPose.[bi.index], v.position))

    finish box, bones |> Array.map (fun b -> Render.MeshBoundsInfo(b.Bone, finish b.LocalBounds))

// merge bounding box information from several meshes
let private mergeMeshBounds (bounds: Render.MeshBoundsInfo array array) =
    bounds
    |> Array.collect id
    |> Seq.groupBy (fun b -> b.Bone)
    |> Seq.toArray
    |> Array.map (fun (bone, bounds) ->
        let box =
            bounds
            |> Seq.map (fun b -> b.LocalBounds)
            |> Seq.reduce (fun l r -> Math.AABB(Vector3.Minimize(l.Min, r.Min), Vector3.Maximize(l.Max, r.Max)))

        Render.MeshBoundsInfo(bone, box))

// build packed & optimized meshes from document
let private buildPackedMeshes (doc: Document) conv skeleton =
    // use a constant FVF for now
    let fvf = [|Position; Tangent; Bitangent; Normal; TexCoord 0; SkinningInfo 4|]
    let format = Render.VertexFormat.Pos_TBN_Tex1_Bone4_Packed

    // get all instance nodes
    let instances = doc.Root.Select("/COLLADA/library_visual_scenes//node/instance_geometry | /COLLADA/library_visual_scenes//node/instance_controller")

    // get all meshes
    let fatMeshes = instances |> Array.collect (fun i ->
        FatMeshBuilder.build doc conv i fvf skeleton
        |> Array.map (fun (mesh, material) -> i, mesh, material))

    // build packed & optimized meshes
    fatMeshes
    |> Array.map (fun (inst, mesh, material) ->
        inst, buildOptimizedMesh mesh format, material, buildMeshBounds mesh)

// build mesh fragments
let private buildMeshFragments meshes meshData materials skeleton =
    let dummyInvBindPose = [|Matrix34.Identity|]

    meshes |> Array.mapi (fun idx (inst: XmlNode, mesh, _, bounds) ->
        let (vertexOffset, indexOffset, indexFormat) = Array.get meshData idx

        // create dummy 1-bone skin binding for non-skinned meshes
        let skin =
            match mesh.skin with
            | Some binding -> binding
            | None -> Render.SkinBinding([| skeleton.nodeMap.[inst.ParentNode] |], dummyInvBindPose)

        // create fragment bounds
        let fbounds =
            match mesh.skin with
            | Some binding -> snd bounds
            | None -> [| Render.MeshBoundsInfo(skin.Bones.[0], fst bounds) |]
        
        // build fragment structure
        { new Render.MeshFragment
          with material = Array.get materials idx
          and skin = skin
          and compressionInfo = mesh.compressionInfo
          and vertexFormat = mesh.format
          and indexFormat = indexFormat
          and vertexOffset = vertexOffset
          and indexOffset = indexOffset
          and indexCount = mesh.indices.Length
          and bounds = fbounds })

// build mesh file from dae file
let private build source target = 
    // parse .dae file
    let doc = Document(source)

    // get cached texture/material builders
    let allTextures = Core.Cache (fun id -> TextureBuilder.build doc id)
    let allMaterials = Core.Cache (fun id -> MaterialBuilder.build doc id (allTextures.Get >> snd))

    // get basis converter (convert up axis, skip unit conversion)
    let conv = BasisConverter(doc)

    // export skeleton
    let skeleton = SkeletonBuilder.build doc conv

    // build packed & optimized meshes
    let meshes = buildPackedMeshes doc conv skeleton

    // build merged vertex & index buffers
    let (vertices, indices, meshData) = mergeMeshGeometry (meshes |> Array.map (fun (_, mesh, _, _) -> mesh))
    let vertexBuffer = Render.VertexBuffer(vertices)
    let indexBuffer = Render.IndexBuffer(indices)

    // build materials
    let materials = meshes |> Array.map (fun (_, _, material, _) -> allMaterials.Get material)

    // build mesh fragments
    let fragments = buildMeshFragments meshes meshData materials skeleton
    let bounds = fragments |> Array.map (fun f -> f.bounds) |> mergeMeshBounds

    // build & save mesh
    let mesh = { new Render.Mesh with fragments = fragments and vertices = vertexBuffer and indices = indexBuffer and skeleton = skeleton.data and bounds = bounds }

    Core.Serialization.Save.toFile target mesh

    // return texture list
    allTextures.Pairs |> Seq.map (fun p -> p.Value)

// .dae -> .mesh builder object
let builder = { new Builder("Mesh") with
    // build mesh
    override this.Build task =
        // build mesh and get texture list
        let textures = build task.Sources.[0].Path task.Targets.[0].Path

        // convert texture list to path list and store it; we convert it to texture tasks in post build
        textures
        |> Seq.toArray
        |> Array.map (fun (source, tex) -> source, tex.Path)
        |> box
        |> Some

    // build textures
    override this.PostBuild (task, result) =
        let textures: (string * string) array = unbox result

        for (source, target) in textures do
            Context.Current.Task(Build.Texture.builder, source = Node source, target = Node target)
    }
