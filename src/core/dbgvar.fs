namespace Core

open System.Collections.Generic

module DbgVarManager =
    let private variables = Dictionary<string, obj ref>()

    let add description value =
        lock variables (fun () ->
            match variables.TryGetValue(description) with
            | true, cell -> cell
            | _ ->
                let cell = ref (box value)
                variables.Add(description, cell)
                cell)

    let getVariables () =
        lock variables (fun () -> Seq.toArray variables |> Array.map (fun p -> p.Key, p.Value))

type DbgVar<'T>(defaults: 'T, description: string) =
    // auto-register variable in manager
    let value = DbgVarManager.add description defaults

    // value accessor
    member x.Value: 'T = unbox !value