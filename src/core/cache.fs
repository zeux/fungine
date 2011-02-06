namespace Core

open System.Collections.Concurrent

// generic cache helper
type Cache<'K, 'V when 'K: equality>(creator) =
    let cache = ConcurrentDictionary<'K, 'V>(HashIdentity.Structural)

    // get the value from the cache, creating it as necessary
    member this.Get key =
        cache.GetOrAdd(key, fun key -> creator key)