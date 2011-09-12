module WinUI.PropertyGrid

open System.ComponentModel
open System.Windows
open System.Windows.Controls
open System.Windows.Data
open System.Windows.Media
open System.Windows.Shapes

type private Grid with
    // grid control adding helper
    member this.Add(control, row, column) =
        this.Children.Add(control) |> ignore
        Grid.SetRow(control, row)
        Grid.SetColumn(control, column)

// filter array of (description, object) pairs with search string
let private filter (pattern: string) (data: (string * obj) array) =
    // break pattern into words
    let words = pattern.Split(" \t".ToCharArray())

    // leave entries which contain all words
    data
    |> Array.filter (fun (description, _) ->
        words |> Array.forall (fun w -> description.Contains(w)))

// property accessors
let private (?) data key = data.GetType().GetProperty(key).GetValue(data, null)
let private (?<-) data key value = data.GetType().GetProperty(key).SetValue(data, value, null)

// value proxy with change notifications
type Accessor(data: obj) =
    let event = Event<_, _>()

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member this.PropertyChanged = event.Publish

    member this.Value
        with get () = data?Value
        and set value =
            data?Value <- value
            event.Trigger(this, PropertyChangedEventArgs("Value"))

// value conversion helper
type private Converter<'T, 'U>(forward: 'T -> 'U, backward: 'U -> 'T) =
    let convert value conv =
        try
            box (conv (unbox value))
        with
        | _ -> DependencyProperty.UnsetValue

    interface IValueConverter with
        member this.Convert(value, targetType, parameter, culture) = convert value forward
        member this.ConvertBack(value, targetType, parameter, culture) = convert value backward

// create edit control group from variable reference
let private createControlEdit data converter =
    // text box bound to data
    let edit = TextBox(MinWidth = 64.0)
    edit.SetBinding(TextBox.TextProperty, Data.Binding("Value", Mode = BindingMode.TwoWay, Source = data, Converter = converter, ValidatesOnExceptions = true)) |> ignore

    // force data accept on Enter
    edit.KeyDown.Add (fun args -> if args.Key = Input.Key.Enter then edit.GetBindingExpression(TextBox.TextProperty).UpdateSource())

    edit :> FrameworkElement

// create slider control group from variable reference
let private createControlSlider data stringConverter floatConverter =
    let edit = createControlEdit data stringConverter

    let slider = Slider(Minimum = 0.0, Maximum = 100.0, Margin = Thickness(4.0, 0.0, 4.0, 0.0))
    slider.SetBinding(Slider.ValueProperty, Data.Binding("Value", Mode = BindingMode.TwoWay, Source = data, Converter = floatConverter)) |> ignore

    let grid = Grid()

    grid.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength()))
    grid.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength(1.0, GridUnitType.Star)))

    grid.Add(edit, 0, 0)
    grid.Add(slider, 0, 1)

    grid :> FrameworkElement

// create control from variable reference
let private createControl (data: Accessor) =
    match data.Value with
    | :? bool ->
        // check box bound to data
        let cb = CheckBox()
        cb.SetBinding(CheckBox.IsCheckedProperty, Data.Binding("Value", Mode = BindingMode.TwoWay, Source = data)) |> ignore

        cb :> FrameworkElement

    | :? System.Enum as enum ->
        // combo box bound to data
        let combo = ComboBox(ItemsSource = System.Enum.GetValues(enum.GetType()))
        combo.SetBinding(ComboBox.SelectedItemProperty, Data.Binding("Value", Mode = BindingMode.TwoWay, Source = data)) |> ignore

        combo :> FrameworkElement

    | :? int -> createControlSlider data (Converter(string, int)) (Converter(float, int))
    | :? float32 -> createControlSlider data (Converter(string, float32)) (Converter(float, float32))
    | :? string -> createControlEdit data null
    | _ -> Label(Content = "unknown type") :> FrameworkElement

// create window from variables
let create (variables: (string * obj) array) =
    let grid = Grid()

    grid.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength()))
    grid.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength()))
    grid.ColumnDefinitions.Add(ColumnDefinition(Width = GridLength(1.0, GridUnitType.Star)))

    let groups =
        variables
        |> Seq.map (fun (name, value) ->
            match name.LastIndexOf('/') with
            | i when i > 0 -> name.Substring(0, i), name.Substring(i + 1), value
            | _ -> "", name, value)
        |> Seq.groupBy (fun (group, name, value) -> group)
        |> Seq.sortBy fst

    let row = ref 0
    let nextrow () = incr row; grid.RowDefinitions.Add(RowDefinition(Height = GridLength()))
    let add column control = grid.Add(control, !row - 1, column)

    let background = Brushes.Honeydew
    let headerText = Brushes.LightSteelBlue
    let arrow = Geometry.Parse("M 0 0 L 4 4 L 8 0 Z")

    for (group, items) in groups do
        nextrow ()

        let border = Border(Background = background)
        add 0 border
        Grid.SetColumnSpan(border, 3)

        let path = Path(Fill = headerText, Data = arrow, Margin = Thickness(4., 4., 0., 0.), VerticalAlignment = VerticalAlignment.Center)
        add 0 path

        let header = Label(Content = Documents.Bold(Documents.Run(group)), Foreground = headerText, VerticalAlignment = VerticalAlignment.Center)
        add 1 header
        Grid.SetColumnSpan(header, 2)

        for (_, name, value) in items do
            nextrow ()

            let headerBorder = Border(BorderBrush = background, BorderThickness = Thickness(1.0, 1.0, 0.0, 0.0))

            add 0 (Border(Background = background))
            add 1 (headerBorder)
            add 2 (Border(BorderBrush = background, BorderThickness = Thickness(1.0, 1.0, 0.0, 0.0)))

            add 1 (TextBlock(Text = name, Margin = Thickness(4.0), VerticalAlignment = VerticalAlignment.Center))

            let data = Accessor(value)
            let control = createControl data
            control.Margin <- Thickness(4.0)
            control.VerticalAlignment <- VerticalAlignment.Center

            add 2 control

            (data :> INotifyPropertyChanged).PropertyChanged.Add(fun _ ->
                headerBorder.Background <- if value?DefaultValue = value?Value then Brushes.Transparent else Brushes.Bisque)

    // create new window
    let window = Window(Content = ScrollViewer(Content = grid))

    System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(window)

    window
