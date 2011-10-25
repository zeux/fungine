namespace Core

open System.Collections.Concurrent
open System.Collections.Generic

// generic cache utils
module CacheUtil =
    let inline update (dict: IDictionary<_, _>) key creator =
        let mutable value = Unchecked.defaultof<_>
        if dict.TryGetValue(key, &value) then value
        else
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
