module WinUI.PropertyTree

open System.Windows
open System.Windows.Controls

type private Grid with
    // grid control adding helper
    member this.Add(control, row, column) =
        this.Children.Add(control) |> ignore
        Grid.SetRow(control, row)
        Grid.SetColumn(control, column)

// a node in the property tree
type private TreeNode =
    | Variable of string * obj
    | Group of string * (TreeNode array)

// filter array of (description, object) pairs with search string
let private filter (pattern: string) (data: (string * obj) array) =
    // break pattern into words
    let words = pattern.Split(" \t".ToCharArray())

    // leave entries which contain all words
    data
    |> Array.filter (fun (description, _) ->
        words |> Array.forall (fun w -> description.Contains(w)))

// create tree nodes from an array of (path elements, object) pairs
let rec private buildTreePath (paths: (string array * obj) array) =
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
let rec private buildTree (data: (string * obj) array) =
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

// property accessors
let private (?) data key = data.GetType().GetProperty(key).GetValue(data, null)
let private (?<-) data key value = data.GetType().GetProperty(key).SetValue(data, value, null)

// typed value proxy for text box
type StringAccessor<'T>(data: obj, conv: string -> 'T) =
    member this.Value
        with get () = string (data?Value)
        and set value = data?Value <- box (conv value)
    
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
let private createControl name (data: obj) =
    match data?Value with
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
            // item with background color bound to data changed state
            let item = TreeViewItem(Header = createControl name data, Padding = Thickness(1.0), Margin = Thickness(1.0))
            (data?ValueChanged :?> IEvent<_>).Add(fun _ -> item.Background <- if data?DefaultValue = data?Value then Media.Brushes.Transparent else Media.Brushes.Bisque)

            item
        | Group (name, children) ->
            TreeViewItem(Header = name, IsExpanded = true, ItemsSource = buildTreeViewItems children))

// build tree view items with cache
let private buildTreeView (tree: TreeView) variables =
    if tree.Tag <> box variables then
        tree.ItemsSource <- variables |> buildTree |> buildTreeViewItems
        tree.Tag <- variables

// create window from variables
let create variables =
    // create tree
    let tree = TreeView()
    buildTreeView tree variables

    VirtualizingStackPanel.SetIsVirtualizing(tree, true)

    // create filter box
    let filter_box = TextBox()

    filter_box.TextChanged.Add(fun _ -> buildTreeView tree (variables |> filter filter_box.Text))

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
