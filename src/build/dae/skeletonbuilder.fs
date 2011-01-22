namespace Build.Dae

open System.Xml
open System.Collections.Generic
open Build.Dae.Parse

type Skeleton =
    { data: Render.Skeleton
      node_map: IDictionary<XmlNode, int> }

module SkeletonBuilder =
    let private parseVectorArray (data: float32 array) offset =
        Vector4(data.[offset + 0], data.[offset + 1], data.[offset + 2], data.[offset + 3])

    let parseMatrixArray (data: float32 array) offset =
        assert (parseVectorArray data (offset + 12) = Vector4.UnitW)
        Matrix34(parseVectorArray data offset, parseVectorArray data (offset + 4), parseVectorArray data (offset + 8))

    let parseMatrixNode (node: XmlNode) =
        let d = Build.Dae.Parse.parseFloatArray node.InnerText 16
        parseMatrixArray d 0

    let private getNodeTransformComponent (comp: XmlNode) =
        let data n = Build.Dae.Parse.parseFloatArray comp.InnerText n

        match comp.Name with
        | "translate" -> let d = data 3 in Matrix34.Translation(d.[0], d.[1], d.[2])
        | "rotate" -> let d = data 4 in Matrix34.RotationAxis(Vector3(d.[0], d.[1], d.[2]), d.[3] / 180.0f * float32 System.Math.PI)
        | "scale" -> let d = data 3 in Matrix34.Scaling(d.[0], d.[1], d.[2])
        | "matrix" -> parseMatrixNode comp
        | _ -> Matrix34.Identity

    let private getNodeTransformLocal (node: XmlNode) =
        Array.foldBack (fun comp acc -> (getNodeTransformComponent comp) * acc) [| for n in node.ChildNodes -> n |] Matrix34.Identity

    let build (doc: Document) =
        // get all document nodes in document order (so that parent is always before child)
        let nodes = doc.Root.Select "/COLLADA/library_visual_scenes//node"

        // build node -> index map
        let node_map = nodes |> Array.mapi (fun index node -> node, index) |> dict

        // get local transform matrices
        let transforms = Array.map getNodeTransformLocal nodes

        // get parent indices
        let parents = nodes |> Array.map (fun node ->
            let parent = node.ParentNode
            if parent.Name = "node" then
                node_map.[parent]
            else
                -1)

        // build skeleton object
        { new Skeleton with data = Render.Skeleton(transforms, parents) and node_map = node_map }