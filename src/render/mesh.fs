namespace Render

type SkinBinding(bones: int array, inv_bind_pose: Matrix array) =
    do assert (bones.Length = inv_bind_pose.Length)

    member x.ComputeBoneTransforms (skeleton: Skeleton) =
        Array.map2 (fun bone inv_bind -> inv_bind * (skeleton.AbsoluteTransform bone)) bones inv_bind_pose