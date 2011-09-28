module Build.Geometry.PostTLOptimizerTipsify

open System.Collections.Generic

// vertex-triangle adjacency information
type private Adjacency(indices: int array, vertexCount) =
    let data =
        // get per-vertex triangle count
        let counts = Array.zeroCreate vertexCount
        for idx in indices do counts.[idx] <- counts.[idx] + 1

        // fill triangles
        let data = Array.init vertexCount (fun i -> Array.zeroCreate counts.[i])
        let filled = Array.zeroCreate vertexCount

        for i in 0 .. indices.Length - 1 do
            let idx = indices.[i]
            data.[idx].[filled.[idx]] <- i / 3
            filled.[idx] <- filled.[idx] + 1

        data

    // get triangles adjacent to vertex
    member this.Triangles vertex = data.[vertex]

// update tag-based cache
let inline private updateCache (cacheTags: int array) cacheSize tag index =
    if !tag - cacheTags.[index] > cacheSize then
        cacheTags.[index] <- !tag
        incr tag

// get next vertex from dead-end stack
let private getNextVertexDeadEndStack (deadEnd: List<int>) (liveTriangles: int array) =
    let rec loop () =
        if deadEnd.Count > 0 then
            let vertex = deadEnd.[deadEnd.Count - 1]
            deadEnd.RemoveAt(deadEnd.Count - 1)

            if liveTriangles.[vertex] > 0 then vertex
            else loop ()
        else -1

    loop ()

// get next vertex from live triangles (slow! use input cursor to start linear search from the first non-processed vertex)
let private getNextVertexDeadEndLive (liveTriangles: int array) inputCursor =
    let rec loop () =
        if !inputCursor < liveTriangles.Length then
            let vertex = !inputCursor
            if liveTriangles.[vertex] > 0 then vertex
            else
                incr inputCursor
                loop ()
        else
            -1

    loop ()

// get next vertex in a dead-end situation
let private getNextVertexDeadEnd deadEnd liveTriangles inputCursor =
    match getNextVertexDeadEndStack deadEnd liveTriangles with
    | -1 -> getNextVertexDeadEndLive liveTriangles inputCursor
    | v -> v

// get next vertex from neighbours
let private getNextVertexNeighbour (nextCandidates: List<int>) (liveTriangles: int array) (cacheTags: int array) cacheSize tag =
    let mutable bestCandidate = -1
    let mutable bestPriority = -1

    for vertex in nextCandidates do
        // otherwise we don't need to process it
        if liveTriangles.[vertex] > 0 then
            // will it be in cache after fanning?
            let cachePosition = tag - cacheTags.[vertex]
            let priority = if 2 * liveTriangles.[vertex] + cachePosition <= cacheSize then cachePosition else 0

            if priority > bestPriority then
                bestCandidate <- vertex
                bestPriority <- priority

    bestCandidate

// optimize indices for specified cache size, return indices and cluster array (can be used for overdraw optimization)
let private optimizeInternal indices vertexCount cacheSize =
    let adjacency = Adjacency(indices, vertexCount)

    // initialize helper data for getNextVertex (reserve worst case size for dead end stack)
    let deadEnd = List<_>(capacity = indices.Length)
    let liveTriangles = Array.init vertexCount (fun i -> (adjacency.Triangles i).Length)
    let cacheTags = Array.zeroCreate vertexCount

    // track emitted flag per triangle
    let emitted = Array.zeroCreate (indices.Length / 3)

    // prepare clusters (reserve worst case size)
    let clusters = List<_>(capacity = indices.Length)
    clusters.Add(0)

    // all vertices are not in cache initially
    let tag = ref (cacheSize + 1)

    // vertex to restart from in case of dead-end
    let inputCursor = ref 1

    let result = List<int>(capacity = indices.Length)
    let nextCandidates = List<int>()

    let rec loop vertex =
        nextCandidates.Clear()

        // emit all vertex neighbours
        for triangle in adjacency.Triangles vertex do
            if not emitted.[triangle] then
                emitted.[triangle] <- true

                // update vertices
                for i in 0 .. 2 do
                    let idx = indices.[triangle * 3 + i]

                    result.Add(idx)
                    deadEnd.Add(idx)
                    nextCandidates.Add(idx)
                    liveTriangles.[idx] <- liveTriangles.[idx] - 1
                    updateCache cacheTags cacheSize tag idx

        // get next vertex
        match getNextVertexNeighbour nextCandidates liveTriangles cacheTags cacheSize !tag with
        | -1 ->
            match getNextVertexDeadEnd deadEnd liveTriangles inputCursor with
            | -1 -> ()
            | v ->
                // hard boundary, add cluster information
                clusters.Add(result.Count / 3)
                loop v
        | v -> loop v

    // process all triangles
    loop 0

    assert (result.Count = indices.Length)
    result.ToArray(), clusters.ToArray()

// get cluster information (centroid, normal, area)
let private getClusterInfo (indices: int array) (positions: Vector3 array) (cbegin, cend) =
    assert (cbegin < cend)

    // get center, unnormalized normal and area for each triangle
    let triangles = Array.init (cend - cbegin) (fun i ->
        let a = indices.[(cbegin + i) * 3 + 0]
        let b = indices.[(cbegin + i) * 3 + 1]
        let c = indices.[(cbegin + i) * 3 + 2]
        let normal = Vector3.Cross(positions.[b] - positions.[a], positions.[c] - positions.[a])
        (positions.[a] + positions.[b] + positions.[c]) / 3.f, normal, normal.Length)

    // get cluster centroid and normal (triangle area weighted sum) and area
    let area = triangles |> Array.sumBy (fun (c, n, a) -> a)
    let centroid = triangles |> Array.sumBy (fun (c, n, a) -> c * a)
    let normal = triangles |> Array.sumBy (fun (c, n, a) -> n)

    centroid / area, Vector3.Normalize(normal), area

// get cluster occlusion potentials from a set of clusters
let private getClusterPotentials (indices: int array) positions clusters =
    // get cluster information for all clusters
    let clusterData = clusters |> Array.map (getClusterInfo indices positions)

    // get cluster order, defined as dot(centroid - meshCentroid, normal)
    let area = clusterData |> Array.sumBy (fun (c, n, a) -> a)
    let centroid = (clusterData |> Array.sumBy (fun (c, n, a) -> c * a)) / area

    clusterData |> Array.map (fun (c, n, a) -> Vector3.Dot(c - centroid, n))

// optimize overdraw order based on cluster ordering heuristic
let private optimizeOverdrawOrder (indices: int array) positions clusters =
    // get cluster triangle ranges
    let ranges = clusters |> Array.mapi (fun i b -> b, if i + 1 < clusters.Length then clusters.[i + 1] else indices.Length / 3)

    // sort clusters by occlusion potential
    let potentials = getClusterPotentials indices positions ranges
    let order = Array.init clusters.Length id |> Array.sortBy (fun i -> -potentials.[i])

    // output cluster indices
    order |> Array.collect (fun i ->
        let (cbegin, cend) = ranges.[i]
        Array.sub indices (cbegin * 3) ((cend - cbegin) * 3))

// calculate ACMR for some prefix of triangle stream (up to specified threshold), return ACMR and the prefix length
let private calculateACMRBounded (indices: int array) rbegin rend cacheTags cacheSize tag threshold =
    // ensures that all vertices are not in cache
    tag := Checked.(+) !tag (cacheSize + 1)

    // remember starting tag to count cache misses
    let tagStart = !tag

    let rec loop face =
        // update cache
        if face < rend / 3 then
            for i in 0 .. 2 do
                let idx = indices.[rbegin + face * 3 + i]
                updateCache cacheTags cacheSize tag idx
    
        // update ACMR and check for threshold
        let acmr = float32 (!tag - tagStart) / float32 (face + 1)

        if face < rend / 3 && acmr > threshold then loop (face + 1)
        else acmr, face + 1

    loop 0

// generate new cluster list by inserting soft boundaries at the points where ACMR is within threshold of the initial value
let private generateSoftClusterBoundaries (indices: int array) (clusters: int array) vertexCount cacheSize threshold =
    // get cluster triangle ranges
    let ranges = clusters |> Array.mapi (fun i b -> b, if i + 1 < clusters.Length then clusters.[i + 1] else indices.Length / 3)

    // initialize cache
    let cacheTags = Array.zeroCreate vertexCount
    let tag = ref 0

    // calculate initial and target acmr
    let acmr, _ = calculateACMRBounded indices 0 indices.Length cacheTags cacheSize tag 0.f
    let acmrThreshold = acmr * (1.f + threshold)

    let result = List<int>(capacity = clusters.Length)

    for (cbegin, cend) in ranges do
        let rec loop start =
            if start < cend then
                let acmr, face = calculateACMRBounded indices start cend cacheTags cacheSize tag acmrThreshold
                result.Add(start)
                loop (start + face * 3)

        loop cbegin

    result.ToArray()

// optimize indices for specified cache size
let optimize indices cacheSize =
    let vertexCount = 1 + Array.max indices
    let result, clusters = optimizeInternal indices vertexCount cacheSize
    result

// optimize indices for specified cache size with overdraw-aware reordering (with no more than specified ACMR penalty)
let optimizeOverdraw indices positions cacheSize acmrThreshold =
    // get initial index and cluster list
    let vertexCount = 1 + Array.max indices
    let result, clusters = optimizeInternal indices vertexCount cacheSize

    // refine cluster list by adding soft boundaries
    let clusters =
        if acmrThreshold <= 0.f then clusters
        else generateSoftClusterBoundaries result clusters vertexCount cacheSize acmrThreshold

    // generate optimized index list
    optimizeOverdrawOrder result positions clusters