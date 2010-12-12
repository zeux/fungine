module Build.Geometry.PostTLAnalyzer

type Result =
    { hits: int
      misses: int
      acmr: float // transformed vertices / triangle count
      atvr: float // transformed vertices / total vertices
    }

let private result misses index_count vertex_count =
    let triangle_count = index_count / 3
    { new Result with hits = index_count - misses and misses = misses and acmr = float misses / float triangle_count and atvr = float misses / float vertex_count }

let analyzeFIFO indices cache_size =
    // timestamp difference is <= cache_size iff the vertex is in cache
    let vertex_count = 1 + Array.max indices
    let vertices = Array.zeroCreate vertex_count
    let mutable timestamp = cache_size + 1

    // count cache misses
    let mutable misses = 0

    for i in indices do
        if timestamp - vertices.[i] > cache_size then
            vertices.[i] <- timestamp
            timestamp <- timestamp + 1
            misses <- misses + 1

    result misses indices.Length vertex_count

let analyzeLRU indices cache_size =
    let vertex_count = 1 + Array.max indices
    let cache_indices = Array.create cache_size vertex_count
    let cache_timestamps = Array.create cache_size 0
    let mutable timestamp = 0

    // count cache misses
    let mutable misses = 0

    for i in indices do
        let hit = Array.exists (fun ci -> ci = i) cache_indices

        // find the cache entry (either the found one, or the least recently used one)
        let index =
            if hit then
                Array.findIndex (fun ci -> ci = i) cache_indices
            else
                let entry = Array.min cache_timestamps
                Array.findIndex (fun ct -> ct = entry) cache_timestamps
            
        // replace the entry and update timestamp
        timestamp <- timestamp + 1
        cache_indices.[index] <- i
        cache_timestamps.[index] <- timestamp

        // update miss count
        if not hit then
            misses <- misses + 1

    result misses indices.Length vertex_count