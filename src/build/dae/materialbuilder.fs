module Build.Dae.MaterialBuilder

open System.Xml

open Build.Dae.Parse

// get texture with the specified name
let private buildTexture (effect: XmlNode) (nodes: XmlNode array) name textureCreator =
    // look for node parameter
    let param = nodes |> Array.tryPick (fun n ->
        if n.Name = name then
            n.ChildNodes |> Seq.cast<XmlNode> |> Seq.tryFind (fun c -> c.Name = "texture")
        else
            None)

    // parse image reference from texture parameter
    match param with
    | Some node ->
        let sampler = effect.SelectSingleNode (sprintf "profile_COMMON/newparam[@sid='%s']" (node.Attribute "texture"))
        let surfaceSid = sampler.SelectSingleNode "sampler2D/source/text() | samplerCUBE/source/text()"
        let surface = effect.SelectSingleNode (sprintf "profile_COMMON/newparam[@sid='%s']" surfaceSid.Value)
        let image = surface.SelectSingleNode "surface/init_from/text() | surface/init_cube/all/@ref"

        Some (textureCreator image.Value)
    | None -> None

// build material
let build (doc: Document) id textureCreator =
    // get material node
    let material = doc.Node id

    // get effect node
    let effect = doc.Node (material.SelectSingleNode("instance_effect/@url").Value)

    // get all parameter nodes
    let subnodes = [|"lambert"; "blinn"; "phong"; "extra/technique"|]
    let nodes = effect.Select (subnodes |> Array.map (sprintf "profile_COMMON/technique/%s/*") |> String.concat " | ")

    // get textures
    let albedoMap = buildTexture effect nodes "diffuse" textureCreator
    let normalMap = buildTexture effect nodes "bump" textureCreator
    let specularMap = buildTexture effect nodes "specular" textureCreator

    // build material
    { new Render.Material with albedoMap = albedoMap and normalMap = normalMap and specularMap = specularMap }
