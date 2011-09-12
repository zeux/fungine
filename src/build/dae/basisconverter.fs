namespace Build.Dae

open Build.Dae.Parse

// helper class for converting from source file basis to the target one
type BasisConverter(transform: Matrix34) =
    // inverse transform for matrix conversion
    let transformInv = Matrix34.Inverse(transform)

    // build identity coverter
    new () = BasisConverter(Matrix34.Identity)

    // build converter from document
    new (doc: Document, ?scale) =
        // convert units
        let unit = doc.Root.SelectSingleNode "/COLLADA/asset/unit/@meter"
        let unitTransform =
            match scale with
            | Some value -> Matrix34.Scaling(float32 unit.Value * value)
            | None -> Matrix34.Identity

        // convert up axis to Z and change handedness (COLLADA is RH)
        let upAxis = doc.Root.SelectSingleNode("/COLLADA/asset/up_axis/text()")

        let axisTransform =
            match upAxis.Value with
            | null -> Matrix34.Identity
            | "Y_UP" -> Matrix34(Vector4.UnitX, Vector4.UnitZ, Vector4.UnitY)
            | "Z_UP" -> Matrix34.Scaling(1.f, -1.f, 1.f)
            | _ -> failwithf "Unknown up axis %s" upAxis.Value

        BasisConverter (unitTransform * axisTransform)

    // convert matrix
    member this.Matrix (m: Matrix34) =
        transform * m * transformInv

    // convert position vector
    member this.Position (v: Vector3) =
        Matrix34.TransformPosition(transform, v)
    
    // convert direction vector
    member this.Direction (v: Vector3) =
        Vector3.Normalize(Matrix34.TransformDirection(transform, v))
