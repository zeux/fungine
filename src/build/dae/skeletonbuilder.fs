module Build.Dae.SkeletonBuilder

open System.Xml

open SlimDX

open Build.Dae.Parse

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

let getNodeTransformLocal (node: XmlNode) =
    Array.foldBack (fun comp acc -> acc * getNodeTransformComponent comp) [| for n in node.ChildNodes -> n |] Matrix.Identity

let rec getNodeTransformAbsolute (node: XmlNode) =
    if node.Name = "node" then
        (getNodeTransformLocal node) * (getNodeTransformAbsolute node.ParentNode)
    else
        Matrix.Identity