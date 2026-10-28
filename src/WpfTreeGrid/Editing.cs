using System.ComponentModel;
using System.Windows.Controls;

namespace WpfTreeGrid;

/// <summary>What opens a cell's editor with the mouse. Typing and F2 always do.</summary>
public enum EditTrigger
{
    /// <summary>A single click on a cell.</summary>
    OnTap,

    /// <summary>A double-click on a cell. The default.</summary>
    OnDoubleTap,
}

/// <summary>
/// A cell position: the header row is 0 and the visible rows count from 1;
/// columns count from 0, or from 1 when the row header shows. See <see cref="TreeGridIndexResolver"/>.
/// </summary>
public readonly record struct RowColumnIndex(int RowIndex, int ColumnIndex)
{
    public static RowColumnIndex Empty { get; } = new(-1, -1);

    public bool IsEmpty => this == Empty;
}

/// <summary>A cell by row item and column, as <c>GetSelectedCells</c> and <c>CurrentCellInfo</c> report it.</summary>
public sealed class TreeGridCellInfo(DataGridColumn? column, object? rowData) : IEquatable<TreeGridCellInfo>
{
    public DataGridColumn? Column { get; } = column;

    public object? RowData { get; } = rowData;

    /// <summary>-1 for a data cell; use <see cref="TreeGridIndexResolver.ResolveToRowIndex(TreeDataGrid, object)"/>.</summary>
    public int RowIndex => -1;

    public bool IsDataRowCell => RowData is not null;

    public bool Equals(TreeGridCellInfo? other) =>
        other is not null && ReferenceEquals(Column, other.Column) && ReferenceEquals(RowData, other.RowData);

    public override bool Equals(object? obj) => Equals(obj as TreeGridCellInfo);

    public override int GetHashCode() => HashCode.Combine(Column, RowData);
}

/// <summary>Raised before a cell's editor opens; set <see cref="CancelEventArgs.Cancel"/> to keep it closed.</summary>
public sealed class TreeGridCurrentCellBeginEditEventArgs(RowColumnIndex rowColumnIndex, DataGridColumn column) : CancelEventArgs
{
    public RowColumnIndex RowColumnIndex { get; } = rowColumnIndex;

    public DataGridColumn Column { get; } = column;
}

/// <summary>Raised when a cell's editor closes, committed or cancelled, after the value is written to the row.</summary>
public sealed class CurrentCellEndEditEventArgs(RowColumnIndex rowColumnIndex) : EventArgs
{
    public RowColumnIndex RowColumnIndex { get; } = rowColumnIndex;
}

/// <summary>
/// The grid's selection controller, which holds its <see cref="CurrentCellManager"/>:
/// <c>grid.SelectionController.CurrentCellManager.EndEdit()</c>.
/// </summary>
public sealed class TreeGridBaseSelectionController
{
    internal TreeGridBaseSelectionController(TreeDataGrid grid) => CurrentCellManager = new TreeGridCurrentCellManager(grid);

    public TreeGridCurrentCellManager CurrentCellManager { get; }
}

/// <summary>The current cell and its editor.</summary>
public sealed class TreeGridCurrentCellManager
{
    private readonly TreeDataGrid _grid;

    internal TreeGridCurrentCellManager(TreeDataGrid grid) => _grid = grid;

    public bool HasCurrentCell => !CurrentRowColumnIndex.IsEmpty;

    public RowColumnIndex CurrentRowColumnIndex =>
        _grid.CurrentCell is { IsValid: true } cell ? _grid.IndexOf(cell.Item, cell.Column) : RowColumnIndex.Empty;

    /// <summary>The current cell, or null when there is none.</summary>
    public TreeGridCurrentCell? CurrentCell => HasCurrentCell
        ? new TreeGridCurrentCell(CurrentRowColumnIndex, _grid.CurrentCell.Column, _grid.IsCurrentCellEditing)
        : null;

    /// <summary>Opens the current cell's editor; false when there is no current cell or it can't be edited.</summary>
    public bool BeginEdit() => _grid.BeginCurrentCellEdit();

    /// <summary>Closes the open editor, writing the value when <paramref name="canCommit"/>; false when none was open.</summary>
    public bool EndEdit(bool canCommit = true) => _grid.EndCurrentCellEdit(canCommit);
}

/// <summary>The current cell as <see cref="TreeGridCurrentCellManager.CurrentCell"/> reports it, at the time it was read.</summary>
public sealed class TreeGridCurrentCell(RowColumnIndex index, DataGridColumn column, bool isEditing)
{
    public int RowIndex { get; } = index.RowIndex;

    public int ColumnIndex { get; } = index.ColumnIndex;

    public DataGridColumn TreeGridColumn { get; } = column;

    public bool IsEditing { get; } = isEditing;
}

/// <summary>
/// Converts between rows, nodes and row and column indexes (header row 0, visible rows
/// from 1; columns shifted by one when the row header shows). Column indexes refer to positions in
/// <see cref="DataGrid.Columns"/>.
/// </summary>
public static class TreeGridIndexResolver
{
    public static int GetHeaderIndex(TreeDataGrid grid) => 0;

    public static int ResolveToStartColumnIndex(TreeDataGrid grid) => grid.ShowRowHeader ? 1 : 0;

    /// <summary>The row index of a visible item; -1 when it is collapsed away, filtered out or not in the grid.</summary>
    public static int ResolveToRowIndex(TreeDataGrid grid, object rowData)
    {
        var index = grid.View.Rows.IndexOf(rowData);
        return index < 0 ? -1 : index + 1;
    }

    public static int ResolveToRowIndex(TreeDataGrid grid, TreeNode node) => ResolveToRowIndex(grid, node.Item);

    /// <summary>The row index of the visible row at <paramref name="nodeIndex"/>.</summary>
    public static int ResolveToRowIndex(TreeDataGrid grid, int nodeIndex) => nodeIndex + 1;

    /// <summary>The position among the visible rows of <paramref name="rowIndex"/>; -1 for the header.</summary>
    public static int ResolveToNodeIndex(TreeDataGrid grid, int rowIndex) => rowIndex < 1 ? -1 : rowIndex - 1;

    public static TreeNode? GetNodeAtRowIndex(TreeDataGrid grid, int rowIndex)
    {
        var index = ResolveToNodeIndex(grid, rowIndex);
        var nodes = grid.View.Rows.Nodes;
        return index >= 0 && index < nodes.Count ? nodes[index] : null;
    }

    /// <summary>The position in <see cref="DataGrid.Columns"/> of a column index; -1 for the row header.</summary>
    public static int ResolveToGridVisibleColumnIndex(TreeDataGrid grid, int columnIndex) => columnIndex - ResolveToStartColumnIndex(grid);

    /// <summary>The column index of the column at <paramref name="gridColumnIndex"/> in <see cref="DataGrid.Columns"/>.</summary>
    public static int ResolveToScrollColumnIndex(TreeDataGrid grid, int gridColumnIndex) => gridColumnIndex + ResolveToStartColumnIndex(grid);
}
