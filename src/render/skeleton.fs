namespace Render

// skeleton proto (mesh node hierarchy)
type Skeleton(parents: int array, names: string array) =
    do assert (parents.Length = names.Length)

    // get bone count
    member this.Length = parents.Length

    // get node parent index, or -1 if root
    member this.Parent index = parents.[index]

    // get node name
    member this.Name index = names.[index]

// skeleton instance (actual transform data)
type SkeletonInstance(proto: Skeleton, transforms: Matrix34 array) =
    do assert (transforms.Length = proto.Length)

    // get proto
    member this.Proto = proto

    // get local node transform
    member this.LocalTransform index = transforms.[index]

    // get absolute node transform
    member this.AbsoluteTransform index =
        let parent = proto.Parent index
        if parent = -1 then
            transforms.[index]
        else
            (this.AbsoluteTransform parent) * transforms.[index]
    