module Build.Geometry.PreTLOptimizer

let optimize (vertices: byte array) (indices: int array) vertex_size =
    // create vertex remap
    let vertex_count = vertices.Length / vertex_size
    let remap = Array.create vertex_count -1
    let mutable vertex_index = 0

    // generate vertex ids in the order of occurence
    for i in indices do
        if remap.[i] < 0 then
            remap.[i] <- vertex_index
            vertex_index <- vertex_index + 1

    assert (vertex_index = vertex_count)

    // remap vertices
    let remapped_vertices = Array.zeroCreate vertices.Length

    for i in 0 .. vertex_count - 1 do
        Array.blit vertices (i * vertex_size) remapped_vertices (remap.[i] * vertex_size) vertex_size

    // remap indices
    let remapped_indices = Array.map (fun i -> remap.[i]) indices

    remapped_vertices, remapped_indices