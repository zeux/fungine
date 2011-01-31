module Build.Dae.Export

open System.Diagnostics
open Microsoft.Win32

// maybe get string value for a backslash-delimited path and key name
let private getRegistryValue (reg: RegistryKey) (path: string) key =
    let pathTokens = path.Split('\\')
    let pathKey = Array.fold (fun (key: RegistryKey) subkey -> if key = null then null else key.OpenSubKey(subkey)) reg pathTokens
    let pathValue = if pathKey = null then null else pathKey.GetValue(key) :?> string
    if System.String.IsNullOrEmpty(pathValue) then None else Some(pathValue)

// get the first variant of registry value with the speficied name
let private getRegistryValueVariant variants path name =
    let keys = [for hive in [RegistryHive.CurrentUser; RegistryHive.LocalMachine] do for view in [RegistryView.Registry64; RegistryView.Registry32] -> RegistryKey.OpenBaseKey(hive, view)]
    let variations = [for key in keys do for var in variants -> key, var]
    List.pick (fun (key, var) -> getRegistryValue key (path var) name) variations

// get the highest version of installed Maya, consider both 64 and 32 bit versions
let private getMayaPath versions =
    getRegistryValueVariant versions (sprintf @"SOFTWARE\Autodesk\Maya\%s\Setup\InstallPath") "MAYA_INSTALL_LOCATION"

// highest installed Maya version
let private mayaPath = lazy (getMayaPath ["2011"; "2010"; "2009"; "2008"])

// build .dae file via standalone mayabatch
let private buildMaya (source: string) (target: string) =
    let mayaBatch = mayaPath.Force() + @"\\bin\mayabatch.exe"

    // export script is in the mel file, so source it and run export proc (' are replaced with \" below)
    let command = sprintf "source './src/build/maya_dae_export.mel'; export('%s', '%s');" (source.Replace('\\', '/')) (target.Replace('\\', '/'))

    // start mayabatch.exe with the export command
    let startInfo = ProcessStartInfo(mayaBatch, sprintf "-batch -noAutoloadPlugins -command \"%s\"" (command.Replace("'", "\\\"")),
                        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true)

    // work around Maya multi-threaded evaluation bugs
    startInfo.EnvironmentVariables.Add("MAYA_NO_TBB", "1")

    // launch the process
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

// get the highest version of installed Max, consider both 64 and 32 bit versions
let private getMaxPath versions =
    getRegistryValueVariant versions (sprintf @"SOFTWARE\Autodesk\3dsmax\%s\MAX-1:409") "Installdir"

// highest installed Max version
let private maxPath = lazy (getMaxPath ["12.0"; "11.0"; "10.0"; "9.0"])

// build .dae file via standalone 3dsmax
let private buildMax (source: string) (target: string) =
    let max = maxPath.Force() + @"\\3dsmax.exe"

    // export script is in the ms file, so include it and run export proc (' are replaced with \" below)
    let command = sprintf @"fileIn './src/build/max_dae_export.ms'; export '%s' '%s'" (source.Replace('\\', '/')) (target.Replace('\\', '/'))

    // start 3dsmax.exe with the export command
    let proc = Process.Start(max, sprintf "-q -silent -vn -mip -mxs \"%s\"" (command.Replace("'", "\\\"")))

    // wait for process exit, exit code is always 0 :(
    proc.WaitForExit()
    true

// build .dae file from DCC sources
let build source target =
    match System.IO.FileInfo(source).Extension with
    | ".ma" | ".mb" -> buildMaya source target
    | ".max" -> buildMax source target
    | _ -> failwithf "Build.Dae: source file %s has unknown extension" source
