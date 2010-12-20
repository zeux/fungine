module treeview

open System.Windows
open System.Windows.Controls

type Grid with
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
type private StringAccessor<'T>(data: obj ref, conv: string -> 'T) =
    member x.Value
        with get () = string (!data)
        and set value = data := box (conv value)
    
// create edit control group from variable reference
let private createControlEdit name data =
    let text = TextBlock(Text = name, Margin = Thickness(0.0, 0.0, 4.0, 0.0))
    let edit = TextBox()
    edit.SetBinding(TextBox.TextProperty, Data.Binding("Value", Source = data, ValidatesOnExceptions = true)) |> ignore
    createHorizontalGrid [|text :> UIElement; edit :> UIElement|] :> FrameworkElement

// create control from variable reference
let private createControl name (data: obj ref) =
    match !data with
    | :? bool ->
        let cb = CheckBox(Content = name)
        cb.SetBinding(CheckBox.IsCheckedProperty, Data.Binding("Value", Source = data)) |> ignore
        cb :> FrameworkElement

    | :? int -> createControlEdit name (StringAccessor(data, int))
    | :? float32 -> createControlEdit name (StringAccessor(data, float32))
    | :? string -> createControlEdit name data
    | _ -> TextBlock(Text = name) :> FrameworkElement

// build tree view items from nodes
let rec private buildTreeViewItems nodes =
    nodes |> Seq.map (fun node ->
        match node with
        | Variable (name, data) ->
            TreeViewItem(Header = createControl name data)
        | Group (name, children) ->
            TreeViewItem(Header = name, IsExpanded = true, ItemsSource = buildTreeViewItems children))

let start () =
    let wnd = Window()

    let treeview = TreeView()

    let nodes = Core.DbgVarManager.getVariables() |> buildTree

    treeview.ItemsSource <- buildTreeViewItems nodes

    VirtualizingStackPanel.SetIsVirtualizing(treeview, true)

    Grid.SetIsSharedSizeScope(treeview, true)

    let grid = Grid()

    let filter_box = TextBox()
    filter_box.TextChanged.Add(fun _ ->
        let nodes = Core.DbgVarManager.getVariables() |> filter filter_box.Text |> buildTree
        treeview.ItemsSource <- buildTreeViewItems nodes)

    grid.RowDefinitions.Add(RowDefinition(Height = GridLength.Auto))
    grid.Add(filter_box, 0, 0)

    grid.RowDefinitions.Add(RowDefinition(Height = GridLength(1.0, GridUnitType.Star)))
    grid.Add(treeview, 1, 0)

    wnd.Content <- grid

    filter_box.Focus() |> ignore

    System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(wnd)
    wnd.Show()