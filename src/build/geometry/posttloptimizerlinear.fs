module Build.Geometry.PostTLOptimizerLinear

open System.Collections.Generic

// triangle tuple
type Triangle = int * int * int

// vertex data
type Vertex =
    new(count) =
        { score = 0.f
          active_triangles = 0
          triangles = Array.zeroCreate count }

    val mutable score: float32 // current vertex score (depends on cache position)
    val mutable active_triangles: int // the number of triangles that are not yet added to the resulting sequence

    val triangles: int array // active triangle indices

    // remove triangle from vertex
    member x.RemoveTriangle triangle =
        // update triangle list
        let idx = Array.findIndex (fun i -> i = triangle) x.triangles

        assert (x.active_triangles > 0)
        x.triangles.[idx] <- x.triangles.[x.active_triangles - 1]

        // update active triangles
        x.active_triangles <- x.active_triangles - 1

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

    Array.init (indices.Length / 3) (fun i -> indices.[i * 3 + 0], indices.[i * 3 + 1], indices.[i * 3 + 2])

// build vertex array from index array
let private buildVertices (indices: int array) =
    // get triangle counts for each vertex
    let vertex_count = 1 + Array.max indices
    let triangle_counts = Array.zeroCreate vertex_count

    for i in indices do triangle_counts.[i] <- triangle_counts.[i] + 1

    // create vertex array with uninitialized triangle arrays
    let vertices = Array.init vertex_count (fun i -> Vertex(triangle_counts.[i]))

    // add triangles to vertex lists
    indices |> Array.iteri (fun i v ->
        let triangle = i / 3

        vertices.[v].triangles.[vertices.[v].active_triangles] <- triangle
        vertices.[v].active_triangles <- vertices.[v].active_triangles + 1)

    // compute initial scores
    for v in vertices do
        assert (v.active_triangles = v.triangles.Length)
        v.score <- getVertexScore -1 v.active_triangles

    vertices

// find the index of the maximum value
let private maxIndex (arr: float32 array) =
    let mutable result = 0

    for i in 1 .. arr.Length - 1 do
        if arr.[result] < arr.[i] then
            result <- i

    result

// optimize index list for post T&L cache with linear-speed vertex cache optimization algorithm (http://home.comcast.net/~tom_forsyth/papers/fast_vert_cache_opt.html)
let optimize indices =
    // build triangle and vertex arrays
    let vertices = buildVertices indices
    let triangles = buildTriangles indices
    let triangle_scores = triangles |> Array.map (fun (a, b, c) -> vertices.[a].score + vertices.[b].score + vertices.[c].score)

    // algorithm loop
    let result = List<int>(capacity = indices.Length)

    let mutable best_triangle_prev = -1
    let mutable cache = [||]

    for i in 0 .. triangles.Length - 1 do
        // find triangle with the best score (it should've been found in the previous loop iteration, so this is usually fast)
        let best_triangle_index = if best_triangle_prev < 0 then maxIndex triangle_scores else best_triangle_prev
        let best_triangle = triangles.[best_triangle_index] |> (fun (a, b, c) -> [|a; b; c|])

        // add triangle to output sequence
        result.AddRange(best_triangle)

        // mark triangle so it won't be ever used again
        triangle_scores.[best_triangle_index] <- -infinityf

        // remove the triangle from vertices
        for vi in best_triangle do vertices.[vi].RemoveTriangle best_triangle_index

        // make new vertex cache
        let cache_new = Array.append best_triangle (Array.choose (fun c -> if c = best_triangle.[0] || c = best_triangle.[1] || c = best_triangle.[2] then None else Some c) cache)

        // update vertices and find best triangle
        best_triangle_prev <- -1

        for i in 0 .. cache_new.Length - 1 do
            let v = vertices.[cache_new.[i]]

            // update vertex cache position
            let cache_position = if i < cache_size then i else -1

            // update vertex score
            let score = getVertexScore cache_position v.active_triangles
            let score_diff = score - v.score
            v.score <- score

            // update triangle scores
            for ti in 0 .. v.active_triangles - 1 do
                let t = v.triangles.[ti]
                triangle_scores.[t] <- triangle_scores.[t] + score_diff

                if best_triangle_prev = -1 || triangle_scores.[best_triangle_prev] < triangle_scores.[t] then
                    best_triangle_prev <- t

        // switch to new cache
        cache <- if cache_new.Length < cache_size then cache_new else Array.sub cache_new 0 cache_size

    result.ToArray()