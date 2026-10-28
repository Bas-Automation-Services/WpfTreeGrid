using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfTreeGrid;

/// <summary>
/// The column members shared by all <c>TreeGrid*Column</c> types, so code that
/// configures columns generically has one type to talk to.
/// </summary>
public interface ITreeGridColumn
{
    /// <summary>Property the column shows, sorts and filters on; also the default header.</summary>
    string? MappingName { get; set; }

    string? HeaderText { get; set; }

    /// <summary>Width in pixels; NaN when the column is sized by <see cref="ColumnSizer"/> or automatically.</summary>
    double Width { get; set; }

    double ActualWidth { get; }

    bool AllowEditing { get; set; }

    bool AllowSorting { get; set; }

    bool AllowResizing { get; set; }

    bool AllowFiltering { get; set; }

    bool IsHidden { get; set; }

    double MinimumWidth { get; set; }

    double MaximumWidth { get; set; }

    TreeColumnSizer ColumnSizer { get; set; }

    Style? CellStyle { get; set; }

    ObservableCollection<FilterPredicate> FilterPredicates { get; }
}

/// <summary>
/// The column properties as dependency properties, so they can be bound — header
/// text from a localization binding, <c>IsHidden</c> from the view-model — and shared by the
/// column types through <c>AddOwner</c>. Each maps onto the DataGridColumn property it mirrors.
/// </summary>
public static class TreeGridColumnProperties
{
    public static readonly DependencyProperty MappingNameProperty = Register("MappingName", typeof(string), null);
    public static readonly DependencyProperty HeaderTextProperty = Register("HeaderText", typeof(string), null);
    public static readonly DependencyProperty AllowEditingProperty = Register("AllowEditing", typeof(bool), true);
    public static readonly DependencyProperty AllowSortingProperty = Register("AllowSorting", typeof(bool), true);
    public static readonly DependencyProperty AllowResizingProperty = Register("AllowResizing", typeof(bool), true);
    public static readonly DependencyProperty AllowFilteringProperty = Register("AllowFiltering", typeof(bool), true);
    public static readonly DependencyProperty IsHiddenProperty = Register("IsHidden", typeof(bool), false);
    public static readonly DependencyProperty MinimumWidthProperty = Register("MinimumWidth", typeof(double), double.NaN);
    public static readonly DependencyProperty MaximumWidthProperty = Register("MaximumWidth", typeof(double), double.NaN);
    // Compiled XAML sets Width through a dependency property found by name; without this one it
    // finds DataGridColumn.Width (a DataGridLength) and throws on Width="44".
    public static readonly DependencyProperty WidthProperty = Register("Width", typeof(double), double.NaN);
    public static readonly DependencyProperty ColumnSizerProperty = Register("ColumnSizer", typeof(TreeColumnSizer), TreeColumnSizer.None);

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.RegisterAttached(name, type, typeof(TreeGridColumnProperties), new PropertyMetadata(defaultValue, OnChanged));

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var column = (DataGridColumn)d;
        var p = e.Property;
        if (p == MappingNameProperty)
        {
            var name = (string?)e.NewValue;
            column.SortMemberPath = name;
            // The mapping name doubles as header until HeaderText is set.
            if (column.Header is null || Equals(column.Header, e.OldValue))
                column.Header = name;
            (column as IBindsMappingName)?.OnMappingNameChanged(name);
        }
        else if (p == HeaderTextProperty) column.Header = e.NewValue;
        else if (p == AllowEditingProperty) column.IsReadOnly = !(bool)e.NewValue;
        else if (p == AllowSortingProperty) column.CanUserSort = (bool)e.NewValue;
        else if (p == AllowResizingProperty) column.CanUserResize = (bool)e.NewValue;
        else if (p == AllowFilteringProperty) TreeDataGrid.SetCanUserFilter(column, (bool)e.NewValue);
        else if (p == IsHiddenProperty) column.Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
        else if (p == MinimumWidthProperty) SetOrClear(column, DataGridColumn.MinWidthProperty, (double)e.NewValue);
        else if (p == MaximumWidthProperty) SetOrClear(column, DataGridColumn.MaxWidthProperty, (double)e.NewValue);
        else if (p == WidthProperty && !double.IsNaN((double)e.NewValue)) column.Width = (double)e.NewValue;
        else if (p == ColumnSizerProperty)
        {
            if ((TreeColumnSizer)e.NewValue == TreeColumnSizer.None) column.ClearValue(DataGridColumn.WidthProperty);
            else column.Width = TreeColumns.ToLength((TreeColumnSizer)e.NewValue);
        }
    }

    private static void SetOrClear(DataGridColumn column, DependencyProperty property, double value)
    {
        if (double.IsNaN(value)) column.ClearValue(property);
        else column.SetValue(property, value);
    }
}

internal interface IBindsMappingName
{
    void OnMappingNameChanged(string? mappingName);
}

/// <summary>Text column: a <see cref="DataGridTextColumn"/> that binds to <see cref="MappingName"/>.</summary>
public class TreeGridTextColumn : DataGridTextColumn, ITreeGridColumn, IBindsMappingName
{
    public static readonly DependencyProperty MappingNameProperty = TreeGridColumnProperties.MappingNameProperty.AddOwner(typeof(TreeGridTextColumn));
    public static readonly DependencyProperty HeaderTextProperty = TreeGridColumnProperties.HeaderTextProperty.AddOwner(typeof(TreeGridTextColumn));
    public static readonly DependencyProperty AllowEditingProperty = TreeGridColumnProperties.AllowEditingProperty.AddOwner(typeof(TreeGridTextColumn));
    public static readonly DependencyProperty AllowSortingProperty = TreeGridColumnProperties.AllowSortingProperty.AddOwner(typeof(TreeGridTextColumn));
    public static readonly DependencyProperty AllowResizingProperty = TreeGridColumnProperties.AllowResizingProperty.AddOwner(typeof(TreeGridTextColumn));
    public static readonly DependencyProperty AllowFilteringProperty = TreeGridColumnProperties.AllowFilteringProperty.AddOwner(typeof(TreeGridTextColumn));
    public static readonly DependencyProperty IsHiddenProperty = TreeGridColumnProperties.IsHiddenProperty.AddOwner(typeof(TreeGridTextColumn));
    public static readonly DependencyProperty MinimumWidthProperty = TreeGridColumnProperties.MinimumWidthProperty.AddOwner(typeof(TreeGridTextColumn));
    public static readonly DependencyProperty MaximumWidthProperty = TreeGridColumnProperties.MaximumWidthProperty.AddOwner(typeof(TreeGridTextColumn));
    public static readonly DependencyProperty WidthProperty = TreeGridColumnProperties.WidthProperty.AddOwner(typeof(TreeGridTextColumn));
    public static readonly DependencyProperty ColumnSizerProperty = TreeGridColumnProperties.ColumnSizerProperty.AddOwner(typeof(TreeGridTextColumn));

    private bool _ownsBinding;
    private TextAlignment _textAlignment;

    public string? MappingName { get => (string?)GetValue(MappingNameProperty); set => SetValue(MappingNameProperty, value); }

    public string? HeaderText { get => (string?)GetValue(HeaderTextProperty) ?? Header as string; set => SetValue(HeaderTextProperty, value); }

    /// <summary>The same as <see cref="DataGridBoundColumn.Binding"/>.</summary>
    public BindingBase? DisplayBinding { get => Binding; set => Binding = value; }

    public new double Width { get => TreeColumns.GetWidth(this); set => base.Width = value; }

    public bool AllowEditing { get => !IsReadOnly; set => SetValue(AllowEditingProperty, value); }

    public bool AllowSorting { get => (bool)GetValue(AllowSortingProperty); set => SetValue(AllowSortingProperty, value); }

    public bool AllowResizing { get => (bool)GetValue(AllowResizingProperty); set => SetValue(AllowResizingProperty, value); }

    public bool AllowFiltering { get => (bool)GetValue(AllowFilteringProperty); set => SetValue(AllowFilteringProperty, value); }

    public bool IsHidden { get => (bool)GetValue(IsHiddenProperty); set => SetValue(IsHiddenProperty, value); }

    public double MinimumWidth { get => (double)GetValue(MinimumWidthProperty); set => SetValue(MinimumWidthProperty, value); }

    public double MaximumWidth { get => (double)GetValue(MaximumWidthProperty); set => SetValue(MaximumWidthProperty, value); }

    public TreeColumnSizer ColumnSizer { get => (TreeColumnSizer)GetValue(ColumnSizerProperty); set => SetValue(ColumnSizerProperty, value); }

    public ObservableCollection<FilterPredicate> FilterPredicates => TreeDataGrid.GetFilterPredicates(this);

    public TextAlignment TextAlignment
    {
        get => _textAlignment;
        set
        {
            _textAlignment = value;
            ElementStyle = new Style(typeof(TextBlock), ElementStyle) { Setters = { new Setter(TextBlock.TextAlignmentProperty, value) } };
            EditingElementStyle = new Style(typeof(TextBox), EditingElementStyle) { Setters = { new Setter(TextBox.TextAlignmentProperty, value) } };
        }
    }

    void IBindsMappingName.OnMappingNameChanged(string? mappingName)
    {
        if (Binding is not null && !_ownsBinding) return;
        Binding = mappingName is null ? null : CreateBinding(mappingName);
        _ownsBinding = true;
    }

    /// <summary>The binding made from <see cref="MappingName"/> when no Binding was set explicitly.</summary>
    protected virtual BindingBase CreateBinding(string path) => new Binding(path);

    /// <summary>Re-creates the binding made from <see cref="MappingName"/>, after a setting it depends on changed.</summary>
    protected void RefreshBinding()
    {
        if (_ownsBinding && MappingName is { } path)
            Binding = CreateBinding(path);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        TreeColumns.OnColumnPropertyChanged(this, DataGridOwner, e);
    }

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem) =>
        TreeColumns.Wrap(this, DataGridOwner, base.GenerateElement(cell, dataItem));

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem) =>
        TreeColumns.Wrap(this, DataGridOwner, base.GenerateEditingElement(cell, dataItem));

    // The base implementations expect their own TextBlock/TextBox, not the expander wrapper.
    protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs) =>
        base.PrepareCellForEdit(TreeCell.Unwrap(editingElement), editingEventArgs);

    protected override bool CommitCellEdit(FrameworkElement editingElement) =>
        base.CommitCellEdit(TreeCell.Unwrap(editingElement));

    protected override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue) =>
        base.CancelCellEdit(TreeCell.Unwrap(editingElement), uneditedValue);
}

/// <summary>Numeric column: right-aligned, formatted with <see cref="NumberDecimalDigits"/> decimals.</summary>
public class TreeGridNumericColumn : TreeGridTextColumn
{
    private int _numberDecimalDigits = 2;

    public TreeGridNumericColumn() => TextAlignment = TextAlignment.Right;

    public int NumberDecimalDigits
    {
        get => _numberDecimalDigits;
        set
        {
            _numberDecimalDigits = value;
            RefreshBinding();
        }
    }

    protected override BindingBase CreateBinding(string path) => new Binding(path) { StringFormat = "N" + NumberDecimalDigits };
}

/// <summary>Date column, formatted with <see cref="CustomPattern"/> (short date by default).</summary>
public class TreeGridDateTimeColumn : TreeGridTextColumn
{
    private string _customPattern = "d";

    public string CustomPattern
    {
        get => _customPattern;
        set
        {
            _customPattern = value;
            RefreshBinding();
        }
    }

    protected override BindingBase CreateBinding(string path) => new Binding(path) { StringFormat = "{0:" + CustomPattern + "}" };
}

/// <summary>Check box column: toggles with one click when editable.</summary>
public class TreeGridCheckBoxColumn : DataGridCheckBoxColumn, ITreeGridColumn, IBindsMappingName
{
    public static readonly DependencyProperty MappingNameProperty = TreeGridColumnProperties.MappingNameProperty.AddOwner(typeof(TreeGridCheckBoxColumn));
    public static readonly DependencyProperty HeaderTextProperty = TreeGridColumnProperties.HeaderTextProperty.AddOwner(typeof(TreeGridCheckBoxColumn));
    public static readonly DependencyProperty AllowEditingProperty = TreeGridColumnProperties.AllowEditingProperty.AddOwner(typeof(TreeGridCheckBoxColumn));
    public static readonly DependencyProperty AllowSortingProperty = TreeGridColumnProperties.AllowSortingProperty.AddOwner(typeof(TreeGridCheckBoxColumn));
    public static readonly DependencyProperty AllowResizingProperty = TreeGridColumnProperties.AllowResizingProperty.AddOwner(typeof(TreeGridCheckBoxColumn));
    public static readonly DependencyProperty AllowFilteringProperty = TreeGridColumnProperties.AllowFilteringProperty.AddOwner(typeof(TreeGridCheckBoxColumn));
    public static readonly DependencyProperty IsHiddenProperty = TreeGridColumnProperties.IsHiddenProperty.AddOwner(typeof(TreeGridCheckBoxColumn));
    public static readonly DependencyProperty MinimumWidthProperty = TreeGridColumnProperties.MinimumWidthProperty.AddOwner(typeof(TreeGridCheckBoxColumn));
    public static readonly DependencyProperty MaximumWidthProperty = TreeGridColumnProperties.MaximumWidthProperty.AddOwner(typeof(TreeGridCheckBoxColumn));
    public static readonly DependencyProperty WidthProperty = TreeGridColumnProperties.WidthProperty.AddOwner(typeof(TreeGridCheckBoxColumn));
    public static readonly DependencyProperty ColumnSizerProperty = TreeGridColumnProperties.ColumnSizerProperty.AddOwner(typeof(TreeGridCheckBoxColumn));

    public string? MappingName { get => (string?)GetValue(MappingNameProperty); set => SetValue(MappingNameProperty, value); }

    public string? HeaderText { get => (string?)GetValue(HeaderTextProperty) ?? Header as string; set => SetValue(HeaderTextProperty, value); }

    public new double Width { get => TreeColumns.GetWidth(this); set => base.Width = value; }

    public bool AllowEditing { get => !IsReadOnly; set => SetValue(AllowEditingProperty, value); }

    public bool AllowSorting { get => (bool)GetValue(AllowSortingProperty); set => SetValue(AllowSortingProperty, value); }

    public bool AllowResizing { get => (bool)GetValue(AllowResizingProperty); set => SetValue(AllowResizingProperty, value); }

    public bool AllowFiltering { get => (bool)GetValue(AllowFilteringProperty); set => SetValue(AllowFilteringProperty, value); }

    public bool IsHidden { get => (bool)GetValue(IsHiddenProperty); set => SetValue(IsHiddenProperty, value); }

    public double MinimumWidth { get => (double)GetValue(MinimumWidthProperty); set => SetValue(MinimumWidthProperty, value); }

    public double MaximumWidth { get => (double)GetValue(MaximumWidthProperty); set => SetValue(MaximumWidthProperty, value); }

    public TreeColumnSizer ColumnSizer { get => (TreeColumnSizer)GetValue(ColumnSizerProperty); set => SetValue(ColumnSizerProperty, value); }

    public ObservableCollection<FilterPredicate> FilterPredicates => TreeDataGrid.GetFilterPredicates(this);

    void IBindsMappingName.OnMappingNameChanged(string? mappingName) =>
        Binding ??= mappingName is null ? null : new Binding(mappingName) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        TreeColumns.OnColumnPropertyChanged(this, DataGridOwner, e);
    }

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var element = base.GenerateElement(cell, dataItem);
        // DataGrid's check box ignores clicks until the cell is in edit mode; this one toggles directly.
        element.IsHitTestVisible = !IsReadOnly;
        return TreeColumns.Wrap(this, DataGridOwner, element);
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem) =>
        TreeColumns.Wrap(this, DataGridOwner, base.GenerateEditingElement(cell, dataItem));

    protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs) =>
        base.PrepareCellForEdit(TreeCell.Unwrap(editingElement), editingEventArgs);

    protected override bool CommitCellEdit(FrameworkElement editingElement) =>
        base.CommitCellEdit(TreeCell.Unwrap(editingElement));

    protected override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue) =>
        base.CancelCellEdit(TreeCell.Unwrap(editingElement), uneditedValue);
}

/// <summary>Template column. <see cref="MappingName"/> only drives sorting, filtering and the header.</summary>
public class TreeGridTemplateColumn : DataGridTemplateColumn, ITreeGridColumn
{
    public static readonly DependencyProperty MappingNameProperty = TreeGridColumnProperties.MappingNameProperty.AddOwner(typeof(TreeGridTemplateColumn));
    public static readonly DependencyProperty HeaderTextProperty = TreeGridColumnProperties.HeaderTextProperty.AddOwner(typeof(TreeGridTemplateColumn));
    public static readonly DependencyProperty AllowEditingProperty = TreeGridColumnProperties.AllowEditingProperty.AddOwner(typeof(TreeGridTemplateColumn));
    public static readonly DependencyProperty AllowSortingProperty = TreeGridColumnProperties.AllowSortingProperty.AddOwner(typeof(TreeGridTemplateColumn));
    public static readonly DependencyProperty AllowResizingProperty = TreeGridColumnProperties.AllowResizingProperty.AddOwner(typeof(TreeGridTemplateColumn));
    public static readonly DependencyProperty AllowFilteringProperty = TreeGridColumnProperties.AllowFilteringProperty.AddOwner(typeof(TreeGridTemplateColumn));
    public static readonly DependencyProperty IsHiddenProperty = TreeGridColumnProperties.IsHiddenProperty.AddOwner(typeof(TreeGridTemplateColumn));
    public static readonly DependencyProperty MinimumWidthProperty = TreeGridColumnProperties.MinimumWidthProperty.AddOwner(typeof(TreeGridTemplateColumn));
    public static readonly DependencyProperty MaximumWidthProperty = TreeGridColumnProperties.MaximumWidthProperty.AddOwner(typeof(TreeGridTemplateColumn));
    public static readonly DependencyProperty WidthProperty = TreeGridColumnProperties.WidthProperty.AddOwner(typeof(TreeGridTemplateColumn));
    public static readonly DependencyProperty ColumnSizerProperty = TreeGridColumnProperties.ColumnSizerProperty.AddOwner(typeof(TreeGridTemplateColumn));

    public string? MappingName { get => (string?)GetValue(MappingNameProperty); set => SetValue(MappingNameProperty, value); }

    public string? HeaderText { get => (string?)GetValue(HeaderTextProperty) ?? Header as string; set => SetValue(HeaderTextProperty, value); }

    /// <summary>The same as <see cref="DataGridTemplateColumn.CellEditingTemplate"/>.</summary>
    public DataTemplate? EditTemplate { get => CellEditingTemplate; set => CellEditingTemplate = value; }

    public new double Width { get => TreeColumns.GetWidth(this); set => base.Width = value; }

    public bool AllowEditing { get => !IsReadOnly; set => SetValue(AllowEditingProperty, value); }

    public bool AllowSorting { get => (bool)GetValue(AllowSortingProperty); set => SetValue(AllowSortingProperty, value); }

    public bool AllowResizing { get => (bool)GetValue(AllowResizingProperty); set => SetValue(AllowResizingProperty, value); }

    public bool AllowFiltering { get => (bool)GetValue(AllowFilteringProperty); set => SetValue(AllowFilteringProperty, value); }

    public bool IsHidden { get => (bool)GetValue(IsHiddenProperty); set => SetValue(IsHiddenProperty, value); }

    public double MinimumWidth { get => (double)GetValue(MinimumWidthProperty); set => SetValue(MinimumWidthProperty, value); }

    public double MaximumWidth { get => (double)GetValue(MaximumWidthProperty); set => SetValue(MaximumWidthProperty, value); }

    public TreeColumnSizer ColumnSizer { get => (TreeColumnSizer)GetValue(ColumnSizerProperty); set => SetValue(ColumnSizerProperty, value); }

    public ObservableCollection<FilterPredicate> FilterPredicates => TreeDataGrid.GetFilterPredicates(this);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        TreeColumns.OnColumnPropertyChanged(this, DataGridOwner, e);
    }

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem) =>
        TreeColumns.Wrap(this, DataGridOwner, base.GenerateElement(cell, dataItem));

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem) =>
        TreeColumns.Wrap(this, DataGridOwner, base.GenerateEditingElement(cell, dataItem));

    protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs) =>
        base.PrepareCellForEdit(TreeCell.Unwrap(editingElement), editingEventArgs);

    protected override bool CommitCellEdit(FrameworkElement editingElement) =>
        base.CommitCellEdit(TreeCell.Unwrap(editingElement));

    protected override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue) =>
        base.CancelCellEdit(TreeCell.Unwrap(editingElement), uneditedValue);
}

internal static class TreeColumns
{
    public static FrameworkElement Wrap(DataGridColumn column, DataGrid? owner, FrameworkElement element) =>
        owner is TreeDataGrid grid && grid.IsExpanderColumn(column) ? new TreeCell(grid, element) : element;

    /// <summary>Keeps the grid in step with column settings it derives state from.</summary>
    public static void OnColumnPropertyChanged(DataGridColumn column, DataGrid? owner, DependencyPropertyChangedEventArgs e)
    {
        if (owner is not TreeDataGrid grid) return;
        if (e.Property == TreeDataGrid.CanUserFilterProperty || e.Property == DataGridColumn.SortMemberPathProperty)
            grid.ApplyFilterHeaders();
        else if (e.Property == DataGridColumn.CellStyleProperty)
            grid.BaseOnDefaultCellStyle(column.CellStyle);
    }

    /// <summary>Width: pixels when set as such, otherwise NaN.</summary>
    public static double GetWidth(DataGridColumn column) => column.Width.IsAbsolute ? column.Width.Value : double.NaN;

    public static DataGridLength ToLength(TreeColumnSizer sizer) => sizer switch
    {
        TreeColumnSizer.Auto => DataGridLength.Auto,
        TreeColumnSizer.Star or TreeColumnSizer.FillColumn or TreeColumnSizer.AutoFillColumn => new DataGridLength(1, DataGridLengthUnitType.Star),
        TreeColumnSizer.SizeToCells => DataGridLength.SizeToCells,
        _ => DataGridLength.SizeToHeader,
    };

    public static TreeColumnSizer ToSizer(DataGridLength length) => length.UnitType switch
    {
        DataGridLengthUnitType.Auto => TreeColumnSizer.Auto,
        DataGridLengthUnitType.Star => TreeColumnSizer.Star,
        DataGridLengthUnitType.SizeToCells => TreeColumnSizer.SizeToCells,
        DataGridLengthUnitType.SizeToHeader => TreeColumnSizer.SizeToHeader,
        _ => TreeColumnSizer.None,
    };
}
