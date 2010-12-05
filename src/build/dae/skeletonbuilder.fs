namespace Build.Dae

open System.Xml
open System.Collections.Generic
open Build.Dae.Parse

type Skeleton =
    { data: Render.Skeleton
      id_map: IDictionary<string, int>
      sid_map: IDictionary<string, int> }

module SkeletonBuilder =
    let private getNodeTransformComponent (comp: XmlNode) =
        let data n = Build.Dae.Parse.parseFloatArray comp.InnerText n

        match comp.Name with
        | "translate" -> let d = data 3 in Matrix.Translation(d.[0], d.[1], d.[2])
        | "rotate" -> let d = data 4 in Matrix.RotationAxis(Vector3(d.[0], d.[1], d.[2]), d.[3] / 180.0f * float32 System.Math.PI)
        | "scale" -> let d = data 3 in Matrix.Scaling(d.[0], d.[1], d.[2])
        | "matrix" ->
            let d = data 16
            let mutable m = Matrix()
            for i in 0..15 do m.Item(i % 4, i / 4) <- d.[i]
            m
        | _ -> Matrix.Identity

    let private getNodeTransformLocal (node: XmlNode) =
        Array.foldBack (fun comp acc -> acc * getNodeTransformComponent comp) [| for n in node.ChildNodes -> n |] Matrix.Identity

    let build (doc: Document) =
        // get all document nodes in document order (so that parent is always before child)
        let nodes = doc.Root.Select "/COLLADA/library_visual_scenes//node"

        // build id -> index map (id is obligatory)
        let id_map = nodes |> Array.mapi (fun index node -> node.Attribute "id", index) |> dict

        // build sid -> index map (sid is optional, used for skinning binding)
        let sid_list = nodes |> Array.mapi (fun index node ->
            let sid = node.Attributes.["sid"]
            if sid <> null then Some (sid.Value, index) else None)
        
        let sid_map = Array.choose id sid_list |> dict

        // get local transform matrices
        let transforms = Array.map getNodeTransformLocal nodes

        // get parent indices
        let parents = nodes |> Array.map (fun node ->
            let parent = node.ParentNode
            if parent.Name = "node" then
                id_map.[parent.Attribute "id"]
            else
                -1)

        // build skeleton object
        { new Skeleton with data = Render.Skeleton(transforms, parents) and id_map = id_map and sid_map = sid_map }