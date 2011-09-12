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
let private getUVRemap (materialInstance: XmlNode) =
    materialInstance.Select("bind_vertex_input")
    |> Array.map (fun node ->
        assert (node.Attribute "input_semantic" = "TEXCOORD")
        node.Attribute "semantic", node.Attribute "input_set")
    |> Array.map (fun (sem, set) ->
        match sem with
        | Regex @"TEX(\d+)" i -> int i, int set
        | Regex @"CHANNEL(\d+)" i -> int i - 1, int set
        | _ -> failwithf "Unknown semantic %s" sem)
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
let private getVertexComponents inputs uvRemap fvf =
    // assume that TBN is always for tex0
    let tbnSet = defaultArg (Map.tryFind 0 uvRemap) 0

    // convert inputs to vertex components, where applicable
    let components = inputs |> Array.choose (fun (semantics, set, id, offset) ->
        let comp =
            match semantics, set with
            | "POSITION", 0 -> Some Position
            | "NORMAL", 0 -> Some Normal
            | "COLOR", n -> Some (Color n)
            | "TEXTANGENT", n when n = tbnSet -> Some Tangent
            | "TEXBINORMAL", n when n = tbnSet -> Some Bitangent
            | "TEXCOORD", n -> uvRemap |> Map.tryPick (fun uv set -> if set = n then Some (TexCoord uv) else None)
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
let private buildVertexBuffer (conv: BasisConverter) (indices: int array) indexStride (components: (FatVertexComponent * float32 array * int) array) (skin: Skin option) =
    // create an array with the specified size
    let arrayCreate size = if size > 0 then Array.zeroCreate size else null

    // get color/uv set count
    let colorSets = components |> Array.map (fun (comp, _, _) -> match comp with | Color n -> n + 1 | _ -> 0) |> Array.max
    let uvSets = components |> Array.map (fun (comp, _, _) -> match comp with | TexCoord n -> n + 1 | _ -> 0) |> Array.max

    // build vertex data
    Array.init (indices.Length / indexStride) (fun indexBlock ->
        let indexBlockOffset = indexBlock * indexStride

        let mutable v = FatVertex(color = arrayCreate colorSets, texcoord = arrayCreate uvSets)

        for (comp, data, indexOffset) in components do
            let offset = indices.[indexBlockOffset + indexOffset]

            match comp with
            | Position -> v.position <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2]) |> conv.Position
            | Tangent -> v.tangent <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2]) |> conv.Direction
            | Bitangent -> v.bitangent <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2]) |> conv.Direction
            | Normal -> v.normal <- Vector3(data.[offset * 3 + 0], data.[offset * 3 + 1], data.[offset * 3 + 2]) |> conv.Direction
            | Color n -> v.color.[n] <- Color4(data.[offset * 4 + 0], data.[offset * 4 + 1], data.[offset * 4 + 2], data.[offset * 4 + 3])
            | TexCoord n -> v.texcoord.[n] <- Vector2(data.[offset * 2 + 0], 1.0f - data.[offset * 2 + 1])
            | SkinningInfo _ when skin.IsSome -> v.bones <- skin.Value.vertices.[offset]
            | _ -> failwithf "Unknown vertex component %A" comp

        v)

// build a single mesh
let private buildInternal (doc: Document) (conv: BasisConverter) (geometry: XmlNode) (controller: XmlNode) (materialInstance: XmlNode) fvf skin =
    // get UV remap information
    let uvRemap = getUVRemap materialInstance

    // get triangles node
    let triangles = geometry.SelectSingleNode("mesh/triangles[@material = '" + materialInstance.Attribute "symbol" + "']")

    // get all vertex inputs
    let inputs = getVertexInputs doc triangles

    // get index array stride (why it is not explicitly stated in the file is beyond me)
    let indexStride = 1 + (inputs |> Array.map (fun (semantics, set, id, offset) -> offset) |> Array.max)

    // get components
    let staticComponents = getVertexComponents inputs uvRemap fvf |> Array.map (fun (comp, id, offset) -> comp, (getVertexComponentData doc comp id), offset)
    let skinnedComponents =
        if Option.isSome skin then
            let _, _, positionOffset = staticComponents |> Array.find (fun (comp, data, offset) -> comp = Position)
            let skinningInfo = fvf |> Array.find (fun comp -> match comp with SkinningInfo _ -> true | _ -> false)

            Array.create 1 (skinningInfo, [||], positionOffset)
        else
            [||]

    let components = Array.append staticComponents skinnedComponents

    // get indices of the individual components
    let indices = getIntArray (triangles.SelectSingleNode("p"))
    assert (indices.Length % indexStride = 0)

    // create the vertex buffer
    let vertices = buildVertexBuffer conv indices indexStride components skin

    { new FatMesh with vertices = vertices and skin = if skin.IsSome then Some skin.Value.binding else None }

// build all meshes for <instance_controller> or <instance_geometry> node
let build (doc: Document) (conv: BasisConverter) (instance: XmlNode) fvf skeleton =
    // get controller and shape nodes
    let instanceUrl = instance.Attribute "url"
    let controller = if instance.Name = "instance_controller" then doc.Node instanceUrl else null
    let geometry = doc.Node (if controller <> null then controller.SelectSingleNode("skin/@source").Value else instanceUrl)

    // get skin data (if we have controller and we need skinning info)
    let skin = Array.tryPick (fun comp ->
        match comp with
        | SkinningInfo n when controller <> null -> Some (Build.Dae.SkinBuilder.build doc conv instance skeleton n)
        | _ -> None) fvf

    // get material instances
    let materialInstances = instance.Select("bind_material/technique_common/instance_material")

    // build meshes
    Array.map (fun mi -> buildInternal doc conv geometry controller mi fvf skin, mi.Attribute "target") materialInstances
