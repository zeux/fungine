namespace Render

type Skeleton(transforms: Matrix34 array, parents: int array) =
    do assert (transforms.Length = parents.Length)

    // get node count
    member x.Length = transforms.Length

    // get node parent index, or -1 if root
    member x.Parent index = parents.[index]

    // get local node transform
    member x.LocalTransform index = transforms.[index]

    // get absolute node transform
    member x.AbsoluteTransform index =
        if parents.[index] = -1 then
            transforms.[index]
        else
            (x.AbsoluteTransform parents.[index]) * transforms.[index]
    