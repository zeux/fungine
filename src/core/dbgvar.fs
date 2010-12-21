namespace Core

open System.Collections.Generic

module DbgVars =
    // variable storage
    type Cell(defaults) =
        let mutable value = defaults
        let event = Event<_>()

        // default value accessor
        member x.DefaultValue = defaults

        // value change notifications
        member x.ValueChanged = event.Publish

        // value accessor
        member x.Value
            with get () = value
            and set rhs =
                value <- rhs
                event.Trigger(box x)

    let private variables = Dictionary<string, Cell>()

    let add description defaults =
        lock variables (fun () ->
            match variables.TryGetValue(description) with
            | true, cell ->
                assert (cell.DefaultValue = defaults)
                cell
            | _ ->
                let cell = Cell(box defaults)
                variables.Add(description, cell)
                cell)

    let getVariables () =
        lock variables (fun () -> Seq.toArray variables |> Array.map (fun p -> p.Key, p.Value))

type DbgVar<'T>(defaults: 'T, description: string) =
    // auto-register variable in manager
    let value = DbgVars.add description defaults

    // value accessor
    member x.Value: 'T = unbox value.Value
