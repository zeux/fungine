module Build.Geometry.PostTLOptimizerLinear

open System.Collections.Generic

// triangle tuple
type Triangle = int * int * int

// vertex data
type Vertex =
    new(count) =
        { score = 0.f
          activeTriangles = 0
          triangles = Array.zeroCreate count }

    val mutable score: float32 // current vertex score (depends on cache position)
    val mutable activeTriangles: int // the number of triangles that are not yet added to the resulting sequence

    val triangles: int array // active triangle indices

    // remove triangle from vertex
    member this.RemoveTriangle triangle =
        // update triangle list
        let idx = Array.findIndex (fun i -> i = triangle) this.triangles

        assert (this.activeTriangles > 0)
        this.triangles.[idx] <- this.triangles.[this.activeTriangles - 1]

        // update active triangles
        this.activeTriangles <- this.activeTriangles - 1

// use fixed size LRU cache
let private cacheSize = 32

// precompute vertex score table based on cache position
let private cacheScoreTable =
    let cacheDecayPower = 1.5f
    let lastTriangleScore = 0.75f

    Array.init cacheSize (fun position ->
        if position < 3 then
            lastTriangleScore
        else
            (1.f - float32 (position - 3) / float32 (cacheSize - 3)) ** cacheDecayPower)

// compute vertex score based on cache position and remaining triangle count
let private getVertexScore cachePosition activeTriangles =
    assert (activeTriangles >= 0)

    if activeTriangles = 0 then
        -1.f
    else
        // get score based on cache position
        let baseScore = if cachePosition < 0 then 0.f else cacheScoreTable.[cachePosition]

        // bonus points for having low number of remaining triangles, so we get rid of lone vertices quickly
        let valenceBoostScale = 2.f
        let valenceBoostPower = 0.5f

        baseScore + valenceBoostScale * (float32 activeTriangles ** -valenceBoostPower)

// build triangle array from index array
let private buildTriangles (indices: int array) =
    assert (indices.Length % 3 = 0)

    Array.init (indices.Length / 3) (fun i -> indices.[i * 3 + 0], indices.[i * 3 + 1], indices.[i * 3 + 2])

// build vertex array from index array
let private buildVertices (indices: int array) =
    // get triangle counts for each vertex
    let vertexCount = 1 + Array.max indices
    let triangleCounts = Array.zeroCreate vertexCount

    for i in indices do triangleCounts.[i] <- triangleCounts.[i] + 1

    // create vertex array with uninitialized triangle arrays
    let vertices = Array.init vertexCount (fun i -> Vertex(triangleCounts.[i]))

    // add triangles to vertex lists
    indices |> Array.iteri (fun i v ->
        let triangle = i / 3

        vertices.[v].triangles.[vertices.[v].activeTriangles] <- triangle
        vertices.[v].activeTriangles <- vertices.[v].activeTriangles + 1)

    // compute initial scores
    for v in vertices do
        assert (v.activeTriangles = v.triangles.Length)
        v.score <- getVertexScore -1 v.activeTriangles

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
    let triangleScores = triangles |> Array.map (fun (a, b, c) -> vertices.[a].score + vertices.[b].score + vertices.[c].score)

    // algorithm loop
    let result = List<int>(capacity = indices.Length)

    let mutable bestTrianglePrev = -1
    let mutable cache = [||]

    for i in 0 .. triangles.Length - 1 do
        // find triangle with the best score (it should've been found in the previous loop iteration, so this is usually fast)
        let bestTriangleIndex = if bestTrianglePrev < 0 then maxIndex triangleScores else bestTrianglePrev
        let bestTriangle = triangles.[bestTriangleIndex] |> (fun (a, b, c) -> [|a; b; c|])

        // add triangle to output sequence
        result.AddRange(bestTriangle)

        // mark triangle so it won't be ever used again
        triangleScores.[bestTriangleIndex] <- -infinityf

        // remove the triangle from vertices
        for vi in bestTriangle do vertices.[vi].RemoveTriangle bestTriangleIndex

        // make new vertex cache
        let cacheNew = Array.append bestTriangle (Array.choose (fun c -> if c = bestTriangle.[0] || c = bestTriangle.[1] || c = bestTriangle.[2] then None else Some c) cache)

        // update vertices and find best triangle
        bestTrianglePrev <- -1

        for i in 0 .. cacheNew.Length - 1 do
            let v = vertices.[cacheNew.[i]]

            // update vertex cache position
            let cachePosition = if i < cacheSize then i else -1

            // update vertex score
            let score = getVertexScore cachePosition v.activeTriangles
            let scoreDiff = score - v.score
            v.score <- score

            // update triangle scores
            for ti in 0 .. v.activeTriangles - 1 do
                let t = v.triangles.[ti]
                triangleScores.[t] <- triangleScores.[t] + scoreDiff

                if bestTrianglePrev = -1 || triangleScores.[bestTrianglePrev] < triangleScores.[t] then
                    bestTrianglePrev <- t

        // switch to new cache
        cache <- if cacheNew.Length < cacheSize then cacheNew else Array.sub cacheNew 0 cacheSize

    result.ToArray()
