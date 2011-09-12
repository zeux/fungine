module Build.Geometry.PostTLAnalyzer

// the result of cache efficiency analysis
type Result =
    { hits: int
      misses: int
      acmr: float // transformed vertices / triangle count
      atvr: float // transformed vertices / total vertices
    }

// get cache analysis result from cache miss count
let private result misses indexCount vertexCount =
    let triangleCount = indexCount / 3
    { new Result with hits = indexCount - misses and misses = misses and acmr = float misses / float triangleCount and atvr = float misses / float vertexCount }

// analyze the Post T&L cache efficiency using a FIFO cache model
let analyzeFIFO indices cacheSize =
    // timestamp difference is <= cacheSize iff the vertex is in cache
    let vertexCount = 1 + Array.max indices
    let vertices = Array.zeroCreate vertexCount
    let mutable timestamp = cacheSize + 1

    // count cache misses
    let mutable misses = 0

    for i in indices do
        if timestamp - vertices.[i] > cacheSize then
            vertices.[i] <- timestamp
            timestamp <- timestamp + 1
            misses <- misses + 1

    result misses indices.Length vertexCount

// analyze the Post T&L cache efficiency using an LRU cache model
let analyzeLRU indices cacheSize =
    let vertexCount = 1 + Array.max indices
    let cacheIndices = Array.create cacheSize vertexCount
    let cacheTimestamps = Array.create cacheSize 0
    let mutable timestamp = 0

    // count cache misses
    let mutable misses = 0

    for i in indices do
        let hit = Array.exists (fun ci -> ci = i) cacheIndices

        // find the cache entry (either the found one, or the least recently used one)
        let index =
            if hit then
                Array.findIndex (fun ci -> ci = i) cacheIndices
            else
                let entry = Array.min cacheTimestamps
                Array.findIndex (fun ct -> ct = entry) cacheTimestamps
            
        // replace the entry and update timestamp
        timestamp <- timestamp + 1
        cacheIndices.[index] <- i
        cacheTimestamps.[index] <- timestamp

        // update miss count
        if not hit then
            misses <- misses + 1

    result misses indices.Length vertexCount