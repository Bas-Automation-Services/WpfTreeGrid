using System.Windows;
using System.Windows.Controls;

namespace WpfTreeGrid;

/// <summary>Column sizing for <c>ColumnSizer</c>.</summary>
public enum TreeColumnSizer
{
    None,
    Auto,
    Star,
    SizeToCells,
    SizeToHeader,
    FillColumn,
    AutoFillColumn,
}

/// <summary>
/// Tree-grid names and defaults on top of DataGrid's own properties. Each alias reads and writes the DataGrid property
/// it maps to; they are plain CLR properties, so set them as attributes, not via bindings.
/// </summary>
public partial class TreeDataGrid
{
    static TreeDataGrid()
    {
        // Swap whatever the user binds for the model's flattened rows, keeping DataGrid's own coercion.
        var baseCoerce = ItemsSourceProperty.GetMetadata(typeof(DataGrid)).CoerceValueCallback;
        ItemsSourceProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(null, null,
            (d, value) => ((TreeDataGrid)d).CoerceItemsSource(baseCoerce is null ? value : baseCoerce(d, value))));

        // Tree-grid defaults. AutoGenerateColumns stays off: generated columns can't host the expander.
        IsReadOnlyProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(true));
        AutoGenerateColumnsProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(false));
        CanUserAddRowsProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(false));
        CanUserDeleteRowsProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(false));
        CanUserResizeColumnsProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(false));
        CanUserReorderColumnsProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(false));
        HeadersVisibilityProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(DataGridHeadersVisibility.Column));
        SelectionModeProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(DataGridSelectionMode.Single));
        RowHeightProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(24.0));
        ColumnHeaderHeightProperty.OverrideMetadata(typeof(TreeDataGrid), new FrameworkPropertyMetadata(24.0));
    }

    /// <summary>Whether cells can be edited (default false). Maps to <c>!IsReadOnly</c>.</summary>
    public bool AllowEditing
    {
        get => !IsReadOnly;
        set => IsReadOnly = !value;
    }

    /// <summary>Whether header clicks sort. Maps to <c>CanUserSortColumns</c>.</summary>
    public bool AllowSorting
    {
        get => CanUserSortColumns;
        set => CanUserSortColumns = value;
    }

    /// <summary>Whether columns can be resized (default false). Maps to <c>CanUserResizeColumns</c>.</summary>
    public bool AllowResizingColumns
    {
        get => CanUserResizeColumns;
        set => CanUserResizeColumns = value;
    }

    /// <summary>Whether columns can be dragged to reorder them (default false). Maps to <c>CanUserReorderColumns</c>.</summary>
    public bool AllowDraggingColumns
    {
        get => CanUserReorderColumns;
        set => CanUserReorderColumns = value;
    }

    /// <summary>Whether the row header shows (default false). Maps to <c>HeadersVisibility</c>.</summary>
    public bool ShowRowHeader
    {
        get => HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row);
        set => HeadersVisibility = value ? DataGridHeadersVisibility.All : DataGridHeadersVisibility.Column;
    }

    /// <summary>Header row height (default 24). Maps to <c>ColumnHeaderHeight</c>.</summary>
    public double HeaderRowHeight
    {
        get => ColumnHeaderHeight;
        set => ColumnHeaderHeight = value;
    }

    /// <summary>Default column sizing. Maps to <c>ColumnWidth</c>; Fill modes become star sizing.</summary>
    public TreeColumnSizer ColumnSizer
    {
        get => TreeColumns.ToSizer(ColumnWidth);
        set => ColumnWidth = TreeColumns.ToLength(value);
    }
}
