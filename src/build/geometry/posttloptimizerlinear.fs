module Build.Geometry.PostTLOptimizerLinear

open System.Collections.Generic

[<AllowNullLiteral>]
type Triangle(a: int, b: int, c: int) =
    [<DefaultValue>]
    val mutable score: float32

    member x.A = a
    member x.B = b
    member x.C = c

type Vertex() =
    [<DefaultValue>]
    val mutable cache_position: int

    [<DefaultValue>]
    val mutable score: float32

    [<DefaultValue>]
    val mutable active_triangles: int

    [<DefaultValue>]
    val mutable triangles: int array

// use fixed size LRU cache
let private cache_size = 32

// precompute vertex score table based on cache position
let private cache_score_table =
    let cache_decay_power = 1.5f
    let last_triangle_score = 0.75f

    Array.init cache_size (fun position ->
        if position < 3 then
            last_triangle_score
        else
            (1.f - float32 (position - 3) / float32 (cache_size - 3)) ** cache_decay_power)

// compute vertex score based on cache position and remaining triangle count
let private getVertexScore cache_position active_triangles =
    assert (active_triangles >= 0)

    if active_triangles = 0 then
        -1.f
    else
        // get score based on cache position
        let base_score = if cache_position < 0 then 0.f else cache_score_table.[cache_position]

        // bonus points for having low number of remaining triangles, so we get rid of lone vertices quickly
        let valence_boost_scale = 2.f
        let valence_boost_power = 0.5f

        base_score + valence_boost_scale * (float32 active_triangles ** -valence_boost_power)

// build triangle array from index array
let private buildTriangles (indices: int array) =
    assert (indices.Length % 3 = 0)

    Array.init (indices.Length / 3) (fun i -> Triangle(indices.[i * 3 + 0], indices.[i * 3 + 1], indices.[i * 3 + 2]))

// build vertex array from index array
let private buildVertices (indices: int array) =
    // get triangle counts for each vertex
    let vertex_count = 1 + Array.max indices
    let triangle_counts = Array.zeroCreate vertex_count

    for i in indices do triangle_counts.[i] <- triangle_counts.[i] + 1

    // create vertex array with uninitialized triangle arrays
    let vertices = Array.init vertex_count (fun i -> Vertex(cache_position = -1, triangles = Array.zeroCreate triangle_counts.[i]))

    // add triangles to vertex lists
    indices |> Array.iteri (fun i v ->
        let triangle = i / 3

        vertices.[v].triangles.[vertices.[v].active_triangles] <- triangle
        vertices.[v].active_triangles <- vertices.[v].active_triangles + 1)

    // sanity check
    assert (vertices |> Array.forall (fun v -> v.active_triangles = v.triangles.Length))

    vertices

let optimize indices =
    // build triangle and vertex arrays
    let triangles = buildTriangles indices
    let vertices = buildVertices indices

    // initialize vertex and triangle scores
    vertices |> Array.iter (fun v -> v.score <- getVertexScore v.cache_position v.active_triangles)
    triangles |> Array.iter (fun t -> t.score <- vertices.[t.A].score + vertices.[t.B].score + vertices.[t.C].score)

    // algorithm loop
    let result = List<int>(capacity = indices.Length)

    let mutable best_triangle: Triangle = null
    let mutable cache = [||]

    for i in 0..triangles.Length - 1 do
        // find triangle with the best score (it should've been found in the previous loop iteration, so this is rare)
        if best_triangle = null then
            best_triangle <- triangles |> Array.maxBy (fun t -> t.score)

        let triangle = [|best_triangle.A; best_triangle.B; best_triangle.C|]

        // add triangle to output sequence
        result.AddRange(triangle)

        // mark triangle so it won't be ever used again
        best_triangle.score <- -infinityf

        // update active triangles count
        for v in triangle do
            assert (vertices.[v].active_triangles > 0)
            vertices.[v].active_triangles <- vertices.[v].active_triangles - 1

        // make new vertex cache
        let cache_new = Array.append triangle (Array.choose (fun c -> if c = triangle.[0] || c = triangle.[1] || c = triangle.[2] then None else Some c) cache)

        // update vertices and find best triangle
        for i in 0 .. cache_new.Length - 1 do
            let v = cache_new.[i]

            // update vertex cache position
            vertices.[v].cache_position <- if i < cache_size then i else -1

            // update vertex score
            let score = getVertexScore vertices.[v].cache_position vertices.[v].active_triangles
            let score_diff = score - vertices.[v].score
            vertices.[v].score <- score

            // update triangle scores
            for t in vertices.[v].triangles do
                triangles.[t].score <- triangles.[t].score + score_diff

                if best_triangle.score < triangles.[t].score then
                    best_triangle <- triangles.[t]
                        
        // no best triangle found
        if best_triangle.A = triangle.[0] && best_triangle.B = triangle.[1] && best_triangle.C = triangle.[2] then
            best_triangle <- null

        // switch to new cache
        cache <- if cache_new.Length < cache_size then cache_new else Array.sub cache_new 0 cache_size

    result.ToArray()