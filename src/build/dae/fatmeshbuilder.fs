module Build.Dae.FatMeshBuilder

open System.Xml

// XmlNode XPath helper
type XmlNode with
    member x.Select(expr) =
        let nodes = x.SelectNodes(expr)
        seq { for n in nodes -> n } |> Seq.toArray

// regex active pattern matcher
let private (|Regex|_|) pattern input =
    let r = System.Text.RegularExpressions.Regex(pattern)
    let m = r.Match(input)
    if m.Success && m.Groups.Count > 1 then
        Some m.Groups.[1].Value
    else
        None

// get UV remap information
let private getUVRemap (material_instance: XmlNode) =
    material_instance.Select("bind_vertex_input")
    |> Seq.map (fun node ->
        assert (node.Attributes.["input_semantic"].Value = "TEXCOORD")
        node.Attributes.["semantic"].Value, node.Attributes.["input_set"].Value)
    |> Seq.map (fun (sem, set) ->
        match sem with
        | Regex @"TEX(\d+)" i -> int i, int set
        | _ -> failwith ("Unknown semantic" + sem))
    |> Map.ofSeq

// get vertex input information as (semantics, set, id, offset) tuple
let private getVertexInput (node: XmlNode) offset =
    let set = node.Attributes.["set"]
    node.Attributes.["semantic"].Value, (if set <> null then (int set.Value) else 0), node.Attributes.["source"].Value, offset
    
// get vertex inputs information as (semantics, set, id, offset) list
let private getVertexInputs (file: Build.Dae.Parse.File) (node: XmlNode) =
    node.Select("input")
    |> Array.collect (fun input ->
        let offset = int input.Attributes.["offset"].Value

        if input.Attributes.["semantic"].Value = "VERTEX" then
            // expand VERTEX semantic to all referenced input semantics with the same offset
            let vertices = file.Node input.Attributes.["source"].Value
            vertices.Select("input") |> Array.map (fun input -> getVertexInput input offset)
        else
            [| getVertexInput input offset |])

// build a single mesh
let private buildInternal (file: Build.Dae.Parse.File) (geometry: XmlNode) (controller: XmlNode) (material_instance: XmlNode) =
    // get UV remap information
    let uv_remap = getUVRemap material_instance

    // get triangles node
    let triangles = geometry.SelectSingleNode("mesh/triangles[@material = '" + material_instance.Attributes.["symbol"].Value + "']")

    // get inputs
    let inputs = getVertexInputs file triangles

    // get indices
    let indices = Build.Dae.Parse.getIntArray (triangles.SelectSingleNode("p"))

    0

// build all meshes for <instance_controller> or <instance_geometry> node
let build (file: Build.Dae.Parse.File) (instance: XmlNode) =
    // get controller and shape nodes
    let instance_url = instance.Attributes.["url"].Value
    let controller = if instance.Name = "instance_controller" then file.Node instance_url else null
    let geometry = file.Node (if controller <> null then controller.SelectSingleNode("skin/@source").Value else instance_url)

    // get material instances
    let material_instances = instance.Select("bind_material/technique_common/instance_material")
    let m1 = instance.SelectNodes("bind_material/technique_common/instance_material")

    // build meshes
    Seq.map (buildInternal file geometry controller) material_instances