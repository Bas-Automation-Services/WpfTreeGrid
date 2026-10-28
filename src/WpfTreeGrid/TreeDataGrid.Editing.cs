using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfTreeGrid;

/// <summary>
/// Current-cell and cell-selection API on top of DataGrid's: the CurrentCellBeginEdit
/// and CurrentCellEndEdit events, <see cref="EditTrigger"/>, selecting cells from code and the
/// <see cref="SelectionController"/> with its current-cell manager. Set
/// <c>SelectionUnit="Cell"</c> for Excel-style cell selection.
/// </summary>
public partial class TreeDataGrid
{
    public static readonly DependencyProperty EditTriggerProperty =
        DependencyProperty.Register(nameof(EditTrigger), typeof(EditTrigger), typeof(TreeDataGrid), new PropertyMetadata(EditTrigger.OnDoubleTap));

    private static readonly DependencyPropertyDescriptor IsEditingDescriptor =
        DependencyPropertyDescriptor.FromProperty(DataGridCell.IsEditingProperty, typeof(DataGridCell));

    private TreeGridBaseSelectionController? _selectionController;
    private DataGridCell? _editingCell;
    private DataGridCell? _endedCell;
    private int _editCommandDepth;

    /// <summary>
    /// Raised before a cell's editor opens, from the keyboard, the mouse or <c>CurrentCellManager.BeginEdit()</c>;
    /// set Cancel to keep it closed. Not raised for read-only cells.
    /// </summary>
    public event EventHandler<TreeGridCurrentCellBeginEditEventArgs>? CurrentCellBeginEdit;

    /// <summary>
    /// Raised when the editor closes, committed (Enter, Tab, another cell) or cancelled (Escape),
    /// after the value is written to the row, so handlers read the row's new value.
    /// </summary>
    public event EventHandler<CurrentCellEndEditEventArgs>? CurrentCellEndEdit;

    /// <summary>Which click opens the editor; <see cref="WpfTreeGrid.EditTrigger.OnDoubleTap"/> by default.</summary>
    public EditTrigger EditTrigger
    {
        get => (EditTrigger)GetValue(EditTriggerProperty);
        set => SetValue(EditTriggerProperty, value);
    }

    /// <summary>Holds <c>SelectionController.CurrentCellManager</c>.</summary>
    public TreeGridBaseSelectionController SelectionController => _selectionController ??= new TreeGridBaseSelectionController(this);

    /// <summary>The current cell; null when there is none. Setting it moves the current cell without changing the selection.</summary>
    public TreeGridCellInfo? CurrentCellInfo
    {
        get => CurrentCell is { IsValid: true } cell ? new TreeGridCellInfo(cell.Column, cell.Item) : null;
        set => CurrentCell = value is { Column: { } column, RowData: { } item } ? new DataGridCellInfo(item, column) : default;
    }

    /// <summary>The selected cells, top to bottom and left to right. In row mode, every cell of the selected rows.</summary>
    public List<TreeGridCellInfo> GetSelectedCells()
    {
        var rowOf = RowPositions();
        return SelectedCells
            .Where(c => c.IsValid)
            .OrderBy(c => rowOf.GetValueOrDefault(c.Item, int.MaxValue)).ThenBy(c => c.Column.DisplayIndex)
            .Select(c => new TreeGridCellInfo(c.Column, c.Item))
            .ToList();
    }

    /// <summary>Adds a cell to the selection; it becomes the current cell when there is none.</summary>
    public void SelectCell(object rowData, DataGridColumn column, bool canFocus = true) =>
        SelectCells(rowData, column, rowData, column, canFocus);

    /// <summary>
    /// Adds the block of cells between two corners to the selection, in display order; the first corner
    /// becomes the current cell when there is none. Selects the rows instead in row mode.
    /// </summary>
    public void SelectCells(object startRowData, DataGridColumn startColumn, object endRowData, DataGridColumn endColumn, bool canFocus = true)
    {
        var cells = Block(startRowData, startColumn, endRowData, endColumn);
        if (cells.Count == 0) return;

        if (SelectionMode == DataGridSelectionMode.Single)
        {
            UnselectAllCells();
            cells = [cells[0]];
        }
        if (SelectionUnit == DataGridSelectionUnit.FullRow)
        {
            foreach (var item in cells.Select(c => c.Item).Distinct().Where(i => !SelectedItems.Contains(i)))
                SelectedItems.Add(item);
        }
        else
        {
            foreach (var cell in cells.Where(c => !SelectedCells.Contains(c)))
                SelectedCells.Add(cell);
        }

        if (!CurrentCell.IsValid)
            CurrentCell = new DataGridCellInfo(startRowData, startColumn);
        if (canFocus)
            Focus();
    }

    public void UnselectCell(object rowData, DataGridColumn column) => UnselectCells(rowData, column, rowData, column);

    /// <summary>Removes the block of cells between two corners from the selection; the rows in row mode.</summary>
    public void UnselectCells(object startRowData, DataGridColumn startColumn, object endRowData, DataGridColumn endColumn)
    {
        var cells = Block(startRowData, startColumn, endRowData, endColumn);
        if (SelectionUnit == DataGridSelectionUnit.FullRow)
        {
            foreach (var item in cells.Select(c => c.Item).Distinct())
                SelectedItems.Remove(item);
            return;
        }
        foreach (var cell in cells)
            SelectedCells.Remove(cell);
    }

    /// <summary>Clears the selection. <paramref name="exceptCurrentRow"/> keeps the current cell (its row in row mode) selected; otherwise the current cell goes too.</summary>
    public void ClearSelections(bool exceptCurrentRow)
    {
        if (SelectionUnit == DataGridSelectionUnit.FullRow) UnselectAll();
        else UnselectAllCells();

        if (!exceptCurrentRow)
            CurrentCell = default;
        else if (CurrentCell is { IsValid: true } current)
            SelectCell(current.Item, current.Column, canFocus: false);
    }

    /// <summary>Moves the current cell to <paramref name="rowColumnIndex"/> and selects it, dropping the rest of the selection unless told otherwise.</summary>
    public void MoveCurrentCell(RowColumnIndex rowColumnIndex, bool needToClearSelection = true)
    {
        var item = TreeGridIndexResolver.GetNodeAtRowIndex(this, rowColumnIndex.RowIndex)?.Item;
        var column = Columns.ElementAtOrDefault(TreeGridIndexResolver.ResolveToGridVisibleColumnIndex(this, rowColumnIndex.ColumnIndex));
        if (item is null || column is null) return;

        if (needToClearSelection)
            ClearSelections(exceptCurrentRow: false);
        CurrentCell = new DataGridCellInfo(item, column);
        SelectCell(item, column, canFocus: false);
        ScrollIntoView(item, column);
    }

    internal RowColumnIndex IndexOf(object item, DataGridColumn column) =>
        new(TreeGridIndexResolver.ResolveToRowIndex(this, item), TreeGridIndexResolver.ResolveToScrollColumnIndex(this, Columns.IndexOf(column)));

    internal bool IsCurrentCellEditing => _editingCell is { IsEditing: true };

    internal bool BeginCurrentCellEdit()
    {
        if (CurrentCell is not { IsValid: true } current) return false;
        if (IsCurrentCellEditing) return true;

        // The editor needs the cell's container.
        ScrollIntoView(current.Item, current.Column);
        UpdateLayout();
        return BeginEdit();
    }

    internal bool EndCurrentCellEdit(bool canCommit)
    {
        if (!IsCurrentCellEditing) return false;
        return canCommit ? CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true) : CancelEdit(DataGridEditingUnit.Cell);
    }

    protected override void OnBeginningEdit(DataGridBeginningEditEventArgs e)
    {
        // A click only opens the editor as EditTrigger says; DataGrid opens it on any click on the current cell.
        // A property without a setter can't take the edited value (and its two-way editor binding would throw).
        if (EditTrigger == EditTrigger.OnDoubleTap && e.EditingEventArgs is MouseButtonEventArgs { ClickCount: < 2 }
            || IsReadOnlyMember(e.Row.Item, e.Column))
        {
            e.Cancel = true;
            return;
        }

        base.OnBeginningEdit(e);
        if (e.Cancel) return;

        var args = new TreeGridCurrentCellBeginEditEventArgs(IndexOf(e.Row.Item, e.Column), e.Column);
        CurrentCellBeginEdit?.Invoke(this, args);
        e.Cancel = args.Cancel;
    }

    protected override void OnPreparingCellForEdit(DataGridPreparingCellForEditEventArgs e)
    {
        base.OnPreparingCellForEdit(e);
        if (FindAncestor<DataGridCell>(e.EditingElement) is not { } cell) return;

        // DataGridCell.IsEditing turns false once the value is written (or the edit cancelled), whichever
        // way the edit ends.
        StopTrackingEdit();
        _editingCell = cell;
        IsEditingDescriptor.AddValueChanged(cell, OnEditingCellIsEditingChanged);
    }

    // DataGrid writes edited values to the row only when the whole row commits; a cell commit just
    // validates. This grid has no row edit and writes on every cell commit, so after a cell's edit
    // ends, commit its row too, then raise CurrentCellEndEdit with the row holding the new value.
    // A cancelled cell cancels the row edit instead: the row still holds the discarded text, and every
    // earlier cell of the row was already written when it committed.
    protected override void OnExecutedCommitEdit(ExecutedRoutedEventArgs e) => RunEditCommand(() => base.OnExecutedCommitEdit(e), commit: true);

    protected override void OnExecutedCancelEdit(ExecutedRoutedEventArgs e) => RunEditCommand(() => base.OnExecutedCancelEdit(e), commit: false);

    private void RunEditCommand(Action command, bool commit)
    {
        _editCommandDepth++;
        try
        {
            command();
        }
        finally
        {
            _editCommandDepth--;
        }
        if (_editCommandDepth > 0 || _endedCell is not { } cell) return;

        _endedCell = null;
        if (commit) CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        else CancelEdit(DataGridEditingUnit.Row);
        RaiseCurrentCellEndEdit(cell);
    }

    private void OnEditingCellIsEditingChanged(object? sender, EventArgs e)
    {
        if (sender is not DataGridCell { IsEditing: false } cell) return;
        StopTrackingEdit();

        // Inside a commit or cancel the row is written first; anything else (say, the row scrolled
        // away) ends the edit right here.
        if (_editCommandDepth > 0) _endedCell = cell;
        else RaiseCurrentCellEndEdit(cell);
    }

    private void RaiseCurrentCellEndEdit(DataGridCell cell) =>
        CurrentCellEndEdit?.Invoke(this, new CurrentCellEndEditEventArgs(IndexOf(cell.DataContext, cell.Column)));

    private void StopTrackingEdit()
    {
        if (_editingCell is null) return;
        IsEditingDescriptor.RemoveValueChanged(_editingCell, OnEditingCellIsEditingChanged);
        _editingCell = null;
    }

    // EditTrigger.OnTap: a click on the current cell opens it right away (the mouse-down made it current).
    private void OnCellMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (EditTrigger != EditTrigger.OnTap || Keyboard.Modifiers != ModifierKeys.None) return;
        if (FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject) is not { IsEditing: false, IsReadOnly: false } cell) return;
        if (CurrentCell is { IsValid: true } current && ReferenceEquals(current.Item, cell.DataContext) && current.Column == cell.Column)
            BeginEdit(e);
    }

    private static bool IsReadOnlyMember(object item, DataGridColumn column)
    {
        if (column is not DataGridBoundColumn { Binding: Binding { Path.Path: { Length: > 0 } path } binding }
            || binding.Mode is BindingMode.OneWay or BindingMode.OneTime || binding.Source is not null)
            return false;

        object? target = item;
        PropertyDescriptor? property = null;
        foreach (var part in path.Split('.'))
        {
            if (target is null) return false;
            property = TypeDescriptor.GetProperties(target).Find(part, ignoreCase: false);
            if (property is null) return false;
            target = property.GetValue(target);
        }
        return property!.IsReadOnly;
    }

    private Dictionary<object, int> RowPositions()
    {
        var positions = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var nodes = View.Rows.Nodes;
        for (var i = 0; i < nodes.Count; i++)
            positions[nodes[i].Item] = i;
        return positions;
    }

    // The cells between two corners, by visible row and display column; none when a corner isn't shown.
    private List<DataGridCellInfo> Block(object startRowData, DataGridColumn startColumn, object endRowData, DataGridColumn endColumn)
    {
        var first = View.Rows.IndexOf(startRowData);
        var last = View.Rows.IndexOf(endRowData);
        if (first < 0 || last < 0 || !Columns.Contains(startColumn) || !Columns.Contains(endColumn)) return [];
        if (first > last) (first, last) = (last, first);

        var left = Math.Min(startColumn.DisplayIndex, endColumn.DisplayIndex);
        var right = Math.Max(startColumn.DisplayIndex, endColumn.DisplayIndex);
        var columns = Columns.Where(c => c.DisplayIndex >= left && c.DisplayIndex <= right).OrderBy(c => c.DisplayIndex).ToList();

        var cells = new List<DataGridCellInfo>();
        for (var i = first; i <= last; i++)
            foreach (var column in columns)
                cells.Add(new DataGridCellInfo(View.Rows[i], column));
        return cells;
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null and not T)
            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        return element as T;
    }
}
