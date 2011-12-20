open System.IO
open System.Text.RegularExpressions

open Microsoft.Win32

let replaceRegex (r: string) (newv: string) data =
    Regex.Replace(data, r, newv)

let replaceAttributePart node attr oldv =
    replaceRegex (sprintf "(?<=<%s[^>]*\\s%s=\"[^\"]*)%s(?=[^\"]*\")" node attr (Regex.Escape oldv))

let replaceAttribute node attr oldv =
    replaceRegex (sprintf "(?<=<%s[^>]*\\s%s=\")%s(?=\")" node attr (Regex.Escape oldv))

let patchTarget source target =
    let data = File.ReadAllText(source)

    // Make sure file is not patched
    if data.Contains("@(ReferenceSignature)") then
        failwithf "Error patching file %s: file is already patched" source

    // Patch file
    let result =
        data
        // Insert AssemblySignature import
        |> replaceRegex "</Project>" "    <Import Project=\"AssemblySignature.targets\" />\r\n</Project>"
        // Change CoreCompile inputs to use ReferenceSignature instead of ReferencePath
        |> replaceAttributePart "Target[^>]*\\sName=\"[^\"]*Compile\"" "Inputs" "@(ReferencePath)" "@(ReferenceSignature)"
        // Patch relative imports with absolute for specific cases
        |> replaceAttribute "UsingTask" "AssemblyFile" "FSharp.Build.dll" @"$(MSBuildExtensionsPath32)\..\Microsoft SDKs\F#\3.0\Framework\v4.0\FSharp.Build.dll"
        |> replaceAttribute "Import" "Project" "Microsoft.Common.targets" @"$(MSBuildToolsPath)\Microsoft.Common.targets"

    // Write new file
    File.WriteAllText(target, result)

let patchTargetLocal source =
    patchTarget source (Path.GetFileName(source))

let HKLM = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)

// patch C# targets file
match HKLM.OpenSubKey(@"SOFTWARE\Microsoft\MSBuild\ToolsVersions\4.0") with
| null -> printfn "Warning: can't find reference to Microsoft.CSharp.Targets in registry, skipping patching"
| key -> patchTargetLocal (key.GetValue("MSBuildToolsPath") :?> string + @"\Microsoft.CSharp.Targets")

// patch F# targets file
let (|??) l r = if l = null then r else l

match HKLM.OpenSubKey(@"SOFTWARE\Microsoft\FSharp\3.0\Runtime\v4.0") |?? HKLM.OpenSubKey(@"SOFTWARE\Microsoft\FSharp\2.0\Runtime\v4.0") with
| null -> printfn "Warning: can't find reference to Microsoft.FSharp.Targets in registry, skipping patching"
| key -> patchTargetLocal (key.GetValue("") :?> string + @"\Microsoft.FSharp.Targets")
