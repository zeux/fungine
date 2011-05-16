namespace BuildSystem

// build task
type Task(sources: Node array, targets: Node array, builder: Builder) =
    let uid = targets |> Array.map (fun n -> n.Uid) |> String.concat "|"

    // build sources (also act as dependencies)
    member this.Sources = sources

    // build targets
    member this.Targets = targets

    // builder
    member this.Builder = builder

    // unique id
    member this.Uid = uid

// builder interface
and [<AbstractClass>] Builder(name) =
    // build task, return optional result (post build is called if result is present)
    abstract member Build: Task -> obj option

    // perform post-build processing; this gets called even if build was not called (with previous result)
    abstract member PostBuild: Task * obj -> unit

    // task description, used for output
    abstract member Description: Task -> string

    // default post-build processing: do nothing
    default this.PostBuild(task, result) = ()

    // default description: consists of builder name, source and target paths
    default this.Description task =
        // pretty-print node array
        let str = function
            | [| e: Node |] -> e.Path
            | a -> sprintf "[%s]" (a |> Array.map (fun n -> n.Path) |> String.concat ", ")

        sprintf "[%s] %s => %s" name (str task.Sources) (str task.Targets)