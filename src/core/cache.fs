namespace Core

open System.Collections.Concurrent
open System.Collections.Generic

// generic cache utils
module CacheUtil =
    let update (dict: IDictionary<_, _>) key creator =
        match dict.TryGetValue(key) with
        | true, value -> value
        | _ ->
            let value = creator key
            dict.Add(key, value)
            value

// generic cache helper
type Cache<'K, 'V when 'K: equality>(creator) =
    let cache = Dictionary<'K, 'V>(HashIdentity.Structural)

    // get the cached key/value pairs
    member this.Pairs = cache :> KeyValuePair<'K, 'V> seq

    // get the value from the cache, creating it as necessary
    member this.Get key =
        CacheUtil.update cache key creator

// generic thread-safe cache helper
type ConcurrentCache<'K, 'V when 'K: equality>(creator) =
    let cache = ConcurrentDictionary<'K, 'V>(HashIdentity.Structural)

    // get the cached key/value pairs
    member this.Pairs = cache.ToArray()

    // get the value from the cache, creating it as necessary
    member this.Get key =
        cache.GetOrAdd(key, fun key -> creator key)
