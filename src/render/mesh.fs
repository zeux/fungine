namespace Render

// skin binding data; consists of a subset of skeleton bones and inverse bind pose matrices
type SkinBinding(bones: int array, inv_bind_pose: Matrix34 array) =
    do assert (bones.Length = inv_bind_pose.Length)

    // compute bone-space transforms from the skeleton
    member x.ComputeBoneTransforms (skeleton: Skeleton) =
        Array.map2 (fun bone inv_bind -> (skeleton.AbsoluteTransform bone) * inv_bind) bones inv_bind_pose