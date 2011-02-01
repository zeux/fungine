namespace Build.Dae

open Build.Dae.Parse

// helper class for converting from source file basis to the target one
type BasisConverter(transform: Matrix34) =
    // inverse transform for matrix conversion
    let transform_inv = Matrix34.Inverse(transform)

    // build converter from document
    new (doc: Document, target_unit) =
        // convert units
        let unit = doc.Root.SelectSingleNode "/COLLADA/asset/unit/@meter"
        let unit_transform = Matrix34.Scaling(float32 unit.Value / target_unit)

        // convert up axis to Z and change handedness (COLLADA is RH)
        let up_axis = doc.Root.SelectSingleNode("/COLLADA/asset/up_axis/text()")

        let axis_transform =
            match up_axis.Value with
            | "Y_UP" -> Matrix34(Vector4.UnitX, Vector4.UnitZ, Vector4.UnitY)
            | "Z_UP" -> Matrix34.Scaling(1.f, -1.f, 1.f)
            | _ -> failwithf "Unknown up axis %s" up_axis.Value

        BasisConverter (unit_transform * axis_transform)

    // convert matrix
    member this.Matrix (m: Matrix34) =
        transform * m * transform_inv

    // convert position vector
    member this.Position (v: Vector3) =
        Matrix34.TransformPosition(transform, v)
    
    // convert direction vector
    member this.Direction (v: Vector3) =
        Vector3.Normalize(Matrix34.TransformDirection(transform, v))