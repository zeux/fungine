namespace Build.Dae

open System.Xml
open System.Collections.Generic
open Build.Dae.Parse

type Skeleton =
    { data: Render.Skeleton
      node_map: IDictionary<XmlNode, int> }

module SkeletonBuilder =
    let parseMatrixArray (data: float32 array) offset =
        let mutable m = Matrix()
        for i in 0 .. 15 do m.Item(i % 4, i / 4) <- data.[i + offset]
        m

    let parseMatrixNode (node: XmlNode) =
        let d = Build.Dae.Parse.parseFloatArray node.InnerText 16
        parseMatrixArray d 0

    let private getNodeTransformComponent (comp: XmlNode) =
        let data n = Build.Dae.Parse.parseFloatArray comp.InnerText n

        match comp.Name with
        | "translate" -> let d = data 3 in Matrix.Translation(d.[0], d.[1], d.[2])
        | "rotate" -> let d = data 4 in Matrix.RotationAxis(SlimDX.Vector3(d.[0], d.[1], d.[2]), d.[3] / 180.0f * float32 System.Math.PI)
        | "scale" -> let d = data 3 in Matrix.Scaling(d.[0], d.[1], d.[2])
        | "matrix" -> parseMatrixNode comp
        | _ -> Matrix.Identity

    let private getNodeTransformLocal (node: XmlNode) =
        Array.foldBack (fun comp acc -> acc * getNodeTransformComponent comp) [| for n in node.ChildNodes -> n |] Matrix.Identity

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