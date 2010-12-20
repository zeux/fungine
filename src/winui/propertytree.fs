module WinUI.PropertyTree

open System.Windows
open System.Windows.Controls

type private Grid with
    member x.Add(control, row, column) =
        x.Children.Add(control) |> ignore
        Grid.SetRow(control, row)
        Grid.SetColumn(control, column)

type private TreeNode =
    | Variable of string * obj ref
    | Group of string * (TreeNode array)

// filter array of (description, object) pairs with search string
let private filter (pattern: string) (data: (string * obj ref) array) =
    // break pattern into words
    let words = pattern.Split(" \t".ToCharArray())

    // leave entries which contain all words
    data
    |> Array.filter (fun (description, _) ->
        words |> Array.forall (fun w -> description.Contains(w)))

// create tree nodes from an array of (path elements, object) pairs
let rec private buildTreePath (paths: (string array * obj ref) array) =
    // group descriptions by the first path element and remove it
    let removeHead data = Array.sub data 1 (data.Length - 1)
    let removeHeadAll data = data |> Seq.map (fun (elements, o) -> removeHead elements, o) |> Seq.toArray
    let groups = paths |> Seq.groupBy (fun (elements, _) -> elements.[0]) |> Seq.map (fun (key, value) -> key, removeHeadAll value)
    
    // convert leaf nodes to variables, build groups recursively for everything else
    groups
    |> Seq.toArray
    |> Array.map (fun (key, value) ->
        match value with
        | [| [||], o |] -> Variable(key, o)
        | _ -> Group(key, buildTreePath value))

// create tree nodes from an array of (description, object) pairs
let rec private buildTree (data: (string * obj ref) array) =
    // split descriptions with separator
    let paths = data |> Array.map (fun (description, o) -> description.Split('/'), o)

    // build tree nodes
    buildTreePath paths

// create horizontal grid from controls
let private createHorizontalGrid controls =
    let grid = Grid()

    controls |> Array.iteri (fun i c ->
        grid.ColumnDefinitions.Add(ColumnDefinition())
        grid.Add(c, 0, i))

    grid

// typed value proxy for text box
type StringAccessor<'T>(data: obj ref, conv: string -> 'T) =
    member x.Value
        with get () = string (!data)
        and set value = data := box (conv value)
    
// create named control group
let private createControlGroup name (control: 'a) =
    let text = TextBlock(Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = Thickness(0.0, 0.0, 4.0, 0.0))
    createHorizontalGrid [|text :> UIElement; control :> UIElement|] :> FrameworkElement

// create edit control group from variable reference
let private createControlEdit name data =
    // text box bound to data
    let edit = TextBox(MinWidth = 32.0)
    edit.SetBinding(TextBox.TextProperty, Data.Binding("Value", Source = data, ValidatesOnExceptions = true)) |> ignore

    // force data accept on Enter
    edit.KeyDown.Add (fun args -> if args.Key = Input.Key.Enter then edit.GetBindingExpression(TextBox.TextProperty).UpdateSource())

    createControlGroup name edit

// create control from variable reference
let private createControl name (data: obj ref) =
    match !data with
    | :? bool ->
        // check box bound to data
        let cb = CheckBox(Content = name)
        cb.SetBinding(CheckBox.IsCheckedProperty, Data.Binding("Value", Source = data)) |> ignore

        cb :> FrameworkElement

    | :? System.Enum as enum ->
        // combo box bound to data
        let combo = ComboBox(ItemsSource = System.Enum.GetValues(enum.GetType()))
        combo.SetBinding(ComboBox.SelectedItemProperty, Data.Binding("Value", Source = data)) |> ignore

        createControlGroup name combo

    | :? int -> createControlEdit name (StringAccessor(data, int))
    | :? float32 -> createControlEdit name (StringAccessor(data, float32))
    | :? string -> createControlEdit name data
    | _ -> TextBlock(Text = name) :> FrameworkElement

// build tree view items from nodes
let rec private buildTreeViewItems nodes =
    nodes |> Seq.map (fun node ->
        match node with
        | Variable (name, data) ->
            let c = createControl name data
            TreeViewItem(Header = c, Margin = Thickness(0.0, 1.0, 0.0, 2.0))
        | Group (name, children) ->
            TreeViewItem(Header = name, IsExpanded = true, ItemsSource = buildTreeViewItems children))

// create window from variables
let create variables =
    let build pattern = variables |> filter pattern |> buildTree |> buildTreeViewItems

    // create tree
    let tree = TreeView(ItemsSource = build "")

    VirtualizingStackPanel.SetIsVirtualizing(tree, true)

    // create filter box
    let filter_box = TextBox()

    filter_box.TextChanged.Add(fun _ -> tree.ItemsSource <- build filter_box.Text)

    // add filter box and tree to grid
    let grid = Grid()

    grid.RowDefinitions.Add(RowDefinition(Height = GridLength.Auto))
    grid.Add(filter_box, 0, 0)

    grid.RowDefinitions.Add(RowDefinition(Height = GridLength(1.0, GridUnitType.Star)))
    grid.Add(tree, 1, 0)

    // make sure filter box is focused
    filter_box.Focus() |> ignore

    // create new window
    let window = Window(Content = grid)

    System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(window)

    window