namespace Render

// skeleton data (mesh node hierarchy)
type Skeleton(transforms: Matrix34 array, parents: int array) =
    do assert (transforms.Length = parents.Length)

    // get node count
    member this.Length = transforms.Length

    // get node parent index, or -1 if root
    member this.Parent index = parents.[index]

    // get local node transform
    member this.LocalTransform index = transforms.[index]

    // get absolute node transform
    member this.AbsoluteTransform index =
        if parents.[index] = -1 then
            transforms.[index]
        else
            (this.AbsoluteTransform parents.[index]) * transforms.[index]
    