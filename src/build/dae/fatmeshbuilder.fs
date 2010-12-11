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

// build vertex buffer
let private buildVertexBuffer (indices: int array) index_stride (components: (FatVertexComponent * float32 array * int) array) (skin: Skin option) =
    Array.init (indices.Length / index_stride) (fun index_block ->
        let index_block_offset = index_block * index_stride

        let mutable v = FatVertex()

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
            | Position -> v.position <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2])
            | Tangent -> v.tangent <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2])
            | Bitangent -> v.bitangent <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2])
            | Normal -> v.normal <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2])
            | Color n -> v.color <- add v.color n (Color4(data.[offset * 4 + 0], data.[offset * 4 + 1], data.[offset * 4 + 2], data.[offset * 4 + 3]))
            | TexCoord n -> v.texcoord <- add v.texcoord n (Vector2(data.[offset * 2 + 0], 1.0f - data.[offset * 2 + 1]))
            | SkinningInfo _ when skin.IsSome -> v.bones <- skin.Value.vertices.[offset]
            | _ -> failwith "Unknown vertex component"

        v)

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
    let static_components = getVertexComponents inputs uv_remap fvf |> Array.map (fun (comp, id, offset) -> comp, (getVertexComponentData doc comp id), offset)
    let skinned_components =
        if Option.isSome skin then
            let _, _, position_offset = static_components |> Array.find (fun (comp, data, offset) -> comp = Position)
            let skinning_info = fvf |> Array.find (fun comp -> match comp with SkinningInfo _ -> true | _ -> false)

            Array.create 1 (skinning_info, [||], position_offset)
        else
            [||]

    let components = Array.append static_components skinned_components

    // get indices of the individual components
    let indices = getIntArray (triangles.SelectSingleNode("p"))
    assert (indices.Length % index_stride = 0)

    // create the vertex buffer
    let vertices = buildVertexBuffer indices index_stride components skin

    { new FatMesh with vertices = vertices and skin = if skin.IsSome then Some skin.Value.binding else None }

// build all meshes for <instance_controller> or <instance_geometry> node
let build (doc: Document) (instance: XmlNode) fvf skeleton =
    // get controller and shape nodes
    let instance_url = instance.Attribute "url"
    let controller = if instance.Name = "instance_controller" then doc.Node instance_url else null
    let geometry = doc.Node (if controller <> null then controller.SelectSingleNode("skin/@source").Value else instance_url)

    // get skin data (if we have controller and we need skinning info)
    let skin = Array.tryPick (fun comp ->
        match comp with
        | SkinningInfo n when controller <> null -> Some (Build.Dae.SkinBuilder.build doc instance skeleton n)
        | _ -> None) fvf

    // get material instances
    let material_instances = instance.Select("bind_material/technique_common/instance_material")

    // build meshes
    Array.map (fun mi -> buildInternal doc geometry controller mi fvf skin) material_instances