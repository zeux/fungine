namespace Core

open System.Collections.Concurrent

module DbgVars =
    // variable storage
    type Cell(defaults) =
        let mutable value = defaults

        // default value accessor
        member this.DefaultValue = defaults

        // value accessor
        member this.Value
            with get () = value
            and set rhs = value <- rhs

    // all registered debug variables
    let private variables = ConcurrentDictionary<string, Cell>()

    // register a debug variable
    let add description defaults =
        let cell = variables.GetOrAdd(description, fun _ -> Cell(box defaults))
        assert (cell.DefaultValue = defaults) // in case the cell already existed (the dbgvar was defined in two modules)
        cell

    // get all debug variables
    let getVariables () =
        variables.ToArray() |> Array.map (fun p -> p.Key, p.Value)

// debug variable handle
type DbgVar<'T>(defaults: 'T, description: string) =
    // auto-register variable in manager
    let value = DbgVars.add description defaults

    // value accessor
    member this.Value: 'T = unbox value.Value
