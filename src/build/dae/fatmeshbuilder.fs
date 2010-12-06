module Build.Dae.FatMeshBuilder

open System.Collections.Generic
open System.Xml

open Build.Dae.Parse
open Build.Geometry

// regex active pattern matcher
let private (|Regex|_|) pattern input =
    let m = System.Text.RegularExpressions.Regex.Match(input, pattern)
    if m.Success && m.Groups.Count > 1 then
        Some m.Groups.[1].Value
    else
        None

// get UV remap information (uv index => set index)
let private getUVRemap (material_instance: XmlNode) =
    material_instance.Select("bind_vertex_input")
    |> Array.map (fun node ->
        assert (node.Attribute "input_semantic" = "TEXCOORD")
        node.Attribute "semantic", node.Attribute "input_set")
    |> Array.map (fun (sem, set) ->
        match sem with
        | Regex @"TEX(\d+)" i -> int i, int set
        | _ -> failwith ("Unknown semantic" + sem))
    |> Map.ofSeq

// get vertex input information as (semantics, set, id, offset) tuple
let private getVertexInput (node: XmlNode) offset =
    let set = node.Attributes.["set"]
    node.Attribute "semantic", (if set <> null then (int set.Value) else 0), node.Attribute "source", offset
    
// get vertex inputs information as (semantics, set, id, offset) array
let private getVertexInputs (doc: Document) (node: XmlNode) =
    node.Select("input")
    |> Array.collect (fun input ->
        let offset = int (input.Attribute "offset")

        if input.Attribute "semantic" = "VERTEX" then
            // expand VERTEX semantic to all referenced input semantics with the same offset
            let vertices = doc.Node (input.Attribute "source")
            vertices.Select("input") |> Array.map (fun input -> getVertexInput input offset)
        else
            [| getVertexInput input offset |])

// get vertex components information as (component, id, offset) array
let private getVertexComponents inputs uv_remap fvf =
    // assume that TBN is always for tex0
    let tbn_set = defaultArg (Map.tryFind 0 uv_remap) 0

    // convert inputs to vertex components, where applicable
    let components = inputs |> Array.choose (fun (semantics, set, id, offset) ->
        let comp =
            match semantics, set with
            | "POSITION", 0 -> Some Position
            | "NORMAL", 0 -> Some Normal
            | "COLOR", n -> Some (Color n)
            | "TEXTANGENT", n when n = tbn_set -> Some Tangent
            | "TEXBINORMAL", n when n = tbn_set -> Some Bitangent
            | "TEXCOORD", n -> uv_remap |> Map.tryPick (fun uv set -> if set = n then Some (TexCoord uv) else None)
            | _ -> None

        match comp with
        | Some c -> Some (c, id, offset)
        | None -> None
    )

    // return a requested subset of provided components
    components |> Array.filter (fun (comp, id, offset) -> Array.exists (fun c -> comp = c) fvf)

// get vertex data for a component
let private getVertexComponentData (doc: Document) comp id =
    match comp with
    | Position
    | Tangent
    | Bitangent
    | Normal -> getFloatArray doc id 3
    | Color _ -> getFloatArray doc id 4
    | TexCoord _ -> getFloatArray doc id 2
    | _ -> [||]

// build index buffer as (vertex remap, index buffer) pair
let private buildIndexBuffer (indices: int array) index_stride =
    let index_block_dummy = [|0..index_stride-1|]

    // the remap stores a mapping from index blocks to indices; to avoid splicing the array, we provide a custom comparer
    let index_block_comparer = { new IEqualityComparer<int> with
        override x.Equals(lhs, rhs) =
            let lofs = lhs * index_stride
            let rofs = rhs * index_stride
            index_block_dummy |> Array.forall (fun i -> indices.[lofs + i] = indices.[rofs + i])

        override x.GetHashCode(obj) =
            let ofs = obj * index_stride

            let rec loop i acc =
                if i = index_stride then acc
                else loop (i + 1) (acc * 31 + indices.[ofs + i])

            loop 0 0
        }

    let vertex_remap = Dictionary<int, int>(index_block_comparer)

    let ib = Array.init (indices.Length / index_stride) (fun index_block ->
        // add the block to dictionary, autogenerating index as necessary
        match vertex_remap.TryGetValue(index_block) with
        | true, index -> index
        | false, _ ->
            let index = vertex_remap.Count
            vertex_remap.Add(index_block, index)
            index)

    vertex_remap, ib

// build vertex buffer
let private buildVertexBuffer (vertex_remap: Dictionary<int, int>) (indices: int array) index_stride (components: (FatVertexComponent * float32 array * int) array) =
    let vb: FatVertex array = Array.zeroCreate vertex_remap.Count

    for kvp in vertex_remap do
        let index_block_offset = kvp.Key * index_stride
        let index = kvp.Value

        for (comp, data, index_offset) in components do
            let offset = indices.[index_block_offset + index_offset]

            let add (arr: 'a array) idx value =
                let length = if arr <> null then arr.Length else 0
                if length > idx then
                    arr.[idx] <- value
                    arr
                else
                    Array.init (idx + 1) (fun i -> if i < length then arr.[i] else if i = idx then value else Unchecked.defaultof<'a>)

            match comp with
            | Position -> vb.[index].position <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2])
            | Tangent -> vb.[index].tangent <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2])
            | Bitangent -> vb.[index].bitangent <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2])
            | Normal -> vb.[index].normal <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2])
            | Color n -> vb.[index].color <- add vb.[index].color n (Color4(data.[offset * 4 + 0], data.[offset * 4 + 1], data.[offset * 4 + 2], data.[offset * 4 + 3]))
            | TexCoord n -> vb.[index].texcoord <- add vb.[index].texcoord n (Vector2(data.[offset * 2 + 0], 1.0f - data.[offset * 2 + 1]))
            | _ -> failwith "Unknown vertex component"

    vb

// build a single mesh
let private buildInternal (doc: Document) (geometry: XmlNode) (controller: XmlNode) (material_instance: XmlNode) fvf skin =
    // get UV remap information
    let uv_remap = getUVRemap material_instance

    // get triangles node
    let triangles = geometry.SelectSingleNode("mesh/triangles[@material = '" + material_instance.Attribute "symbol" + "']")

    // get all vertex inputs
    let inputs = getVertexInputs doc triangles

    // get index array stride (why it is not explicitly stated in the file is beyond me)
    let index_stride = 1 + (inputs |> Array.map (fun (semantics, set, id, offset) -> offset) |> Array.max)

    // get components
    let components = getVertexComponents inputs uv_remap fvf |> Array.map (fun (comp, id, offset) -> comp, (getVertexComponentData doc comp id), offset)

    // get indices
    let indices = getIntArray (triangles.SelectSingleNode("p"))
    assert (indices.Length % index_stride = 0)

    // set indices that are not present in the target vertex to 0
    for i in 0..index_stride-1 do
        if Array.tryFind (fun (comp, id, offset) -> i = offset) components |> Option.isNone then
            for idx in i..index_stride..indices.Length-1 do
                indices.[idx] <- 0

    // create the index buffer
    let vertex_remap, ib = buildIndexBuffer indices index_stride

    // create the vertex buffer
    let vb = buildVertexBuffer vertex_remap indices index_stride components

    { new FatMesh with vertices = vb and indices = ib and skin = None }

// build all meshes for <instance_controller> or <instance_geometry> node
let build (doc: Document) (instance: XmlNode) fvf skeleton =
    // get controller and shape nodes
    let instance_url = instance.Attribute "url"
    let controller = if instance.Name = "instance_controller" then doc.Node instance_url else null
    let geometry = doc.Node (if controller <> null then controller.SelectSingleNode("skin/@source").Value else instance_url)

    // get skin data (if we have controller and we need skinning info)
    let skin = Array.tryPick (fun comp ->
        match comp with
        | SkinningInfo n when controller <> null -> Some (Build.Dae.SkinBuilder.build doc controller skeleton n)
        | _ -> None) fvf

    // get material instances
    let material_instances = instance.Select("bind_material/technique_common/instance_material")

    // build meshes
    Array.map (fun mi -> buildInternal doc geometry controller mi fvf skin) material_instances