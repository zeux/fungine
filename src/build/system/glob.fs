module BuildSystem.Glob

// glob pattern format:
// * - any part of entry name (non-recursive)
// ** - any part of path name (recursive)
// / or \ - path separator
// anything else - verbatim case-insensitive path characters
// if a globbing pattern ends with path separator, the pattern matches directories, otherwise it matches files

open System.IO
open System.Text.RegularExpressions

// match path with a glob pattern
let matches (pattern: string) =
    // convert pattern to regular expression
    let expr =
        pattern.Split([|'/'; '\\'|])
        |> Array.map (fun part -> Regex.Escape(part).Replace(@"\*\*", ".*").Replace(@"\*", @"[^/\\]*"))
        |> String.concat @"[/\\]"
    let r = Regex(sprintf "^%s$" expr)

    // return regexp matcher
    fun (path: string) -> r.IsMatch(path.ToLowerInvariant())

// find files/folders using glob pattern
let find (pattern: string) =
    // get path in a canonical form (a**b is the same as a*/**/*b; a canonical entry either equals "**" or does not contain "**")
    let canonical = Regex.Replace(pattern, @"([^/\\])?\*\*([^/\\])?", fun (m: Match) ->
        let (a, b) = m.Groups.[1], m.Groups.[2]
        (if a.Success then a.Value + "*/" else "") + "**" + (if b.Success then "/*" + b.Value else ""))

    // for each path entry, get a path walker and apply them sequentially to perform search
    let rec loop acc entries =
        match entries with
        | ["**"] -> acc >> Array.collect (fun path -> Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        | ["**"; e] when e <> "" -> acc >> Array.collect (fun path -> Directory.GetFiles(path, e, SearchOption.AllDirectories))
        | "**" :: rest -> loop (acc >> Array.collect (fun path -> Array.append [| path |] (Directory.GetDirectories(path, "*", SearchOption.AllDirectories)))) rest
        | [""] -> acc
        | [e] -> acc >> Array.collect (fun path -> Directory.GetFiles(path, e))
        | e :: rest -> loop (acc >> Array.collect (fun path -> Directory.GetDirectories(path, e))) rest
        | [] -> acc

    loop (fun path -> [| path |]) (canonical.Split([|'/'; '\\'|]) |> List.ofArray)