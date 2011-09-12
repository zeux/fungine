namespace Build.Dae

open System.Xml
open System.Collections.Generic
open Build.Dae.Parse

// build-time skeleton type
type Skeleton =
    { data: Render.SkeletonInstance
      nodeMap: IDictionary<XmlNode, int> }

module SkeletonBuilder =
    // convert 4 consecutive floats into Vector4
    let private parseVectorArray (data: float32 array) offset =
        Vector4(data.[offset + 0], data.[offset + 1], data.[offset + 2], data.[offset + 3])

    // convert 16 consecutive floats into Matrix34
    let parseMatrixArray (data: float32 array) offset =
        assert (parseVectorArray data (offset + 12) = Vector4.UnitW)
        Matrix34(parseVectorArray data offset, parseVectorArray data (offset + 4), parseVectorArray data (offset + 8))

    // get Matrix34 from COLLADA node text
    let parseMatrixNode (node: XmlNode) =
        let d = Build.Dae.Parse.parseFloatArray node.InnerText 16
        parseMatrixArray d 0

    // get transformation matrix from COLLADA transform node
    let private getNodeTransformComponent (comp: XmlNode) =
        let data n = Build.Dae.Parse.parseFloatArray comp.InnerText n

        match comp.Name with
        | "translate" -> let d = data 3 in Matrix34.Translation(d.[0], d.[1], d.[2])
        | "rotate" -> let d = data 4 in Matrix34.RotationAxis(Vector3(d.[0], d.[1], d.[2]), d.[3] / 180.0f * float32 System.Math.PI)
        | "scale" -> let d = data 3 in Matrix34.Scaling(d.[0], d.[1], d.[2])
        | "matrix" -> parseMatrixNode comp
        | _ -> Matrix34.Identity

    // get aggregate transformation matrix from COLLADA scene node
    let private getNodeTransformLocal (node: XmlNode) =
        Array.foldBack (fun comp acc -> (getNodeTransformComponent comp) * acc) [| for n in node.ChildNodes -> n |] Matrix34.Identity

    // build skeleton from all scene nodes in the document
    let build (doc: Document) (conv: BasisConverter) =
        // get all document nodes in document order (so that parent is always before child)
        let nodes = doc.Root.Select "/COLLADA/library_visual_scenes//node"

        // build node -> index map
        let nodeMap = nodes |> Array.mapi (fun index node -> node, index) |> dict

        // get local transform matrices
        let transforms = nodes |> Array.map getNodeTransformLocal |> Array.map conv.Matrix

        // get parent indices
        let parents = nodes |> Array.map (fun node ->
            let parent = node.ParentNode
            if parent.Name = "node" then
                nodeMap.[parent]
            else
                -1)

        // get names
        let names = nodes |> Array.map (fun node -> node.Attributes.["name"]) |> Array.map (fun attr -> if attr <> null then attr.Value else null)

        // build skeleton object
        { new Skeleton with data = Render.SkeletonInstance(Render.Skeleton(parents, names), transforms) and nodeMap = nodeMap }
