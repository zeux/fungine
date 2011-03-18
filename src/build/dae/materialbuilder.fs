namespace Build.Dae

open System.Xml

open Build.Dae.Parse

module MaterialBuilder =
    // get texture with the specified name
    let private buildTexture (effect: XmlNode) (nodes: XmlNode array) name texture_creator =
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
            let surface_sid = sampler.SelectSingleNode "sampler2D/source/text() | samplerCUBE/source/text()"
            let surface = effect.SelectSingleNode (sprintf "profile_COMMON/newparam[@sid='%s']" surface_sid.Value)
            let image = surface.SelectSingleNode "surface/init_from/text() | surface/init_cube/all/@ref"

            Some (texture_creator image.Value)
        | None -> None

    // build material
    let build (doc: Document) id texture_creator =
        // get material node
        let material = doc.Node id

        // get effect node
        let effect = doc.Node (material.SelectSingleNode("instance_effect/@url").Value)

        // get all parameter nodes
        let subnodes = [|"lambert"; "blinn"; "phong"; "extra/technique"|]
        let nodes = effect.Select (subnodes |> Array.map (sprintf "profile_COMMON/technique/%s/*") |> String.concat " | ")

        // get textures
        let albedo_map = buildTexture effect nodes "diffuse" texture_creator
        let normal_map = buildTexture effect nodes "bump" texture_creator
        let specular_map = buildTexture effect nodes "specular" texture_creator

        // build material
        { new Render.Material with albedo_map = albedo_map and normal_map = normal_map and specular_map = specular_map }