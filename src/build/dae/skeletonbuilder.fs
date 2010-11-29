module Build.Dae.SkeletonBuilder

open System.Xml

open SlimDX

open Build.Dae.Parse

let private getNodeTransformComponent (comp: XmlNode) =
    let data = lazy(comp.InnerText |> Build.Dae.Parse.splitWhitespace |> Array.map (fun s -> float32 s))
    let d () = data.Force()

    match comp.Name with
    | "translate" -> Matrix.Translation(d().[0], d().[1], d().[2])
    | "rotate" -> Matrix.RotationAxis(Vector3(d().[0], d().[1], d().[2]), d().[3] / 180.0f * float32 System.Math.PI)
    | "scale" -> Matrix.Scaling(d().[0], d().[1], d().[2])
    | "matrix" ->
        let mutable m = Matrix()
        for i in 0..15 do m.Item(i % 4, i / 4) <- d().[i]
        m
    | _ -> Matrix.Identity

let getNodeTransformLocal (node: XmlNode) =
    [| for n in node.ChildNodes -> n |]
    |> Array.rev
    |> Array.fold (fun acc comp -> acc * getNodeTransformComponent comp) Matrix.Identity

let rec getNodeTransformAbsolute (node: XmlNode) =
    if node.Name = "node" then
        (getNodeTransformLocal node) * (getNodeTransformAbsolute node.ParentNode)
    else
        Matrix.Identity