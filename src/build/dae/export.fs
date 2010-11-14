module Build.Dae.Export

open System.Diagnostics
open Microsoft.Win32

// maybe get string value for a backslash-delimited path and key name
let private getRegistryValue (reg: RegistryKey) (path: string) key =
    let pathTokens = path.Split('\\')
    let pathKey = Array.fold (fun (key: RegistryKey) subkey -> if key = null then null else key.OpenSubKey(subkey)) reg pathTokens
    let pathValue = if pathKey = null then null else pathKey.GetValue(key)
    if pathValue = null then None else Some(pathValue :?> string)

// get the highest version of installed Maya, consider both 64 and 32 bit versions
let private getMayaPath versions =
    let keys = [for view in [RegistryView.Registry64; RegistryView.Registry32] -> RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)]
    let variations = [for key in keys do for ver in versions -> key, ver]
    List.pick (fun (key, ver) -> getRegistryValue key (sprintf @"SOFTWARE\Autodesk\Maya\%s\Setup\InstallPath" ver) "MAYA_INSTALL_LOCATION") variations

let private mayaPath = lazy(getMayaPath ["2011"; "2010"; "2009"; "2008"])

let private buildMaya (source: string) (target: string) =
    let mayaBatch = mayaPath.Force() + @"\\bin\mayabatch.exe"

    // export script is in the mel file, so source it and run export proc (' are replaced with \" below)
    let command = sprintf "source './src/build/maya_dae_export.mel'; export('%s', '%s');" (source.Replace('\\', '/')) (target.Replace('\\', '/'))

    // start mayabatch.exe with the export command
    let startInfo = ProcessStartInfo(mayaBatch, sprintf "-batch -noAutoloadPlugins -command \"%s\"" (command.Replace("'", "\\\"")),
                        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true)
    let proc = Process.Start(startInfo)

    // echo process output
    let handler (args: System.Diagnostics.DataReceivedEventArgs) =
        let s = args.Data
        lock proc (fun () -> if s <> null && s.Length > 0 then printfn "Build.Dae[%s]: %s" source s)

    proc.OutputDataReceived.Add(handler)
    proc.BeginOutputReadLine()

    proc.ErrorDataReceived.Add(handler)
    proc.BeginErrorReadLine()

    // wait for process exit, exit code is 0 if export succeeded (see maya_dae_export.mel)
    proc.WaitForExit()
    proc.ExitCode = 0

let build source target =
    match System.IO.FileInfo(source).Extension with
    | ".ma" | ".mb" -> buildMaya source target
    | _ -> failwith ("Build.Dae: source file " + source + " has unknown extension")
