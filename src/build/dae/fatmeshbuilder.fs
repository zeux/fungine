module Build.Dae.FatMeshBuilder

open System.Xml
open Build.Dae.Parse

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
        assert (node.Attribute "input_semantic" = "TEXCOORD")
        node.Attribute "semantic", node.Attribute "input_set")
    |> Seq.map (fun (sem, set) ->
        match sem with
        | Regex @"TEX(\d+)" i -> int i, int set
        | _ -> failwith ("Unknown semantic" + sem))
    |> Map.ofSeq

// get vertex input information as (semantics, set, id, offset) tuple
let private getVertexInput (node: XmlNode) offset =
    let set = node.Attributes.["set"]
    node.Attribute "semantic", (if set <> null then (int set.Value) else 0), node.Attribute "source", offset
    
// get vertex inputs information as (semantics, set, id, offset) list
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

// build a single mesh
let private buildInternal (doc: Document) (geometry: XmlNode) (controller: XmlNode) (material_instance: XmlNode) =
    // get UV remap information
    let uv_remap = getUVRemap material_instance

    // get triangles node
    let triangles = geometry.SelectSingleNode("mesh/triangles[@material = '" + material_instance.Attribute "symbol" + "']")

    // get inputs
    let inputs = getVertexInputs doc triangles

    // get indices
    let indices = getIntArray (triangles.SelectSingleNode("p"))

    0

// build all meshes for <instance_controller> or <instance_geometry> node
let build (doc: Document) (instance: XmlNode) =
    // get controller and shape nodes
    let instance_url = instance.Attribute "url"
    let controller = if instance.Name = "instance_controller" then doc.Node instance_url else null
    let geometry = doc.Node (if controller <> null then controller.SelectSingleNode("skin/@source").Value else instance_url)

    // get material instances
    let material_instances = instance.Select("bind_material/technique_common/instance_material")

    // build meshes
    Seq.map (buildInternal doc geometry controller) material_instances