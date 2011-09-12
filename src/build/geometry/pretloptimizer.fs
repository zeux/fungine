module Build.Geometry.PreTLOptimizer

// optimize vertices and indices for Pre T&L cache efficiency
let optimize (vertices: byte array) (indices: int array) vertexSize =
    // create vertex remap
    let vertexCount = vertices.Length / vertexSize
    let remap = Array.create vertexCount -1
    let mutable vertexIndex = 0

    // generate vertex ids in the order of occurence
    for i in indices do
        if remap.[i] < 0 then
            remap.[i] <- vertexIndex
            vertexIndex <- vertexIndex + 1

    assert (vertexIndex = vertexCount)

    // remap vertices
    let remappedVertices = Array.zeroCreate vertices.Length

    for i in 0 .. vertexCount - 1 do
        Array.blit vertices (i * vertexSize) remappedVertices (remap.[i] * vertexSize) vertexSize

    // remap indices
    let remappedIndices = Array.map (fun i -> remap.[i]) indices

    remappedVertices, remappedIndices