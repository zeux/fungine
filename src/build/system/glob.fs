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