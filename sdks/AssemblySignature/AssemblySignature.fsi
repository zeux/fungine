module AssemblySignature

open Microsoft.Build.Framework
open Microsoft.Build.Utilities

type GenerateAssemblySignature =
    inherit AppDomainIsolatedTask

    new : unit -> GenerateAssemblySignature
    override Execute : unit -> bool

    member Input : ITaskItem with get, set
    member Output : ITaskItem with get, set
    member References : ITaskItem [] with get, set
    member WriteLog : bool with get, set

type GenerateAssemblyDependency =
    inherit Task

    new : unit -> GenerateAssemblyDependency
    override Execute : unit -> bool

    member AssemblyName : string with get, set
    member Output : ITaskItem with get, set
    member References : ITaskItem [] with get, set
