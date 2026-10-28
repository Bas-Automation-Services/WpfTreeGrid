using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfTreeGrid.Tests;

/// <summary>
/// Cell editing: the edit trigger, the keys
/// DataGrid commits and cancels with, and cell selection in row mode.
/// </summary>
public class EditingTests
{
    private readonly Part _bolt = new("Bolt", 1);
    private readonly Part _frame;
    private readonly Part _plate = new("Plate", 8);
    private readonly ObservableCollection<Part> _roots;

    public EditingTests()
    {
        _frame = new Part("Frame", 10, _bolt);
        _roots = [_frame, _plate];
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Window _window;

        public Fixture(ObservableCollection<Part> roots, Action<TreeDataGrid>? configure = null)
        {
            Grid = new TreeDataGrid
            {
                AllowEditing = true,
                SelectionUnit = DataGridSelectionUnit.Cell,
                SelectionMode = DataGridSelectionMode.Extended,
            };
            Grid.Columns.Add(new TreeGridTextColumn { MappingName = nameof(Part.Name) });
            Grid.Columns.Add(new TreeGridNumericColumn { MappingName = nameof(Part.Mass) });
            configure?.Invoke(Grid);
            Grid.ItemsSource = roots;
            Grid.CurrentCellEndEdit += (_, e) =>
                Log.Add($"End {TreeGridIndexResolver.GetNodeAtRowIndex(Grid, e.RowColumnIndex.RowIndex)?.Item}");

            _window = new Window { Content = Grid, Width = 600, Height = 400, Left = -10000, ShowActivated = false, ShowInTaskbar = false };
            _window.Show();
            Pump();
        }

        public TreeDataGrid Grid { get; }

        public List<string> Log { get; } = [];

        public DataGridColumn NameColumn => Grid.Columns[0];

        public void Pump()
        {
            _window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Grid.UpdateLayout();
        }

        public DataGridCell Cell(object item, DataGridColumn column)
        {
            Grid.ScrollIntoView(item, column);
            Pump();
            var row = (DataGridRow)Grid.ItemContainerGenerator.ContainerFromItem(item);
            return (DataGridCell)column.GetCellContent(row).Parent;
        }

        public TextBox Editor() => Descendants<TextBox>(Grid).First(b => b.IsVisible);

        public void Press(UIElement target, Key key) =>
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            });

        public void Dispose() => _window.Close();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static MouseButtonEventArgs Click(int clickCount, RoutedEvent routedEvent)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routedEvent };
        // ClickCount has an internal setter; a real double-click arrives with 2.
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, clickCount);
        return args;
    }

    [StaFact]
    public void EditTriggerDefaultsToDoubleTap()
    {
        Assert.Equal(EditTrigger.OnDoubleTap, new TreeDataGrid().EditTrigger);
    }

    [StaFact]
    public void DoubleTapIgnoresASecondSingleClickButOpensOnADoubleClick()
    {
        // DataGrid opens the editor when the current cell is clicked again; the default
        // OnDoubleTap only opens it on a double-click.
        using var f = new Fixture(_roots);
        f.Grid.CurrentCell = new DataGridCellInfo(_plate, f.NameColumn);

        Assert.False(f.Grid.BeginEdit(Click(1, UIElement.MouseLeftButtonDownEvent)));
        Assert.True(f.Grid.BeginEdit(Click(2, UIElement.MouseLeftButtonDownEvent)));
    }

    [StaFact]
    public void TapOpensTheEditorOnTheFirstClick()
    {
        using var f = new Fixture(_roots, g => g.EditTrigger = EditTrigger.OnTap);
        f.Grid.CurrentCell = new DataGridCellInfo(_plate, f.NameColumn);
        var cell = f.Cell(_plate, f.NameColumn);

        // Real input bubbles Mouse.MouseUp; each element on the way turns it into its own MouseLeftButtonUp.
        cell.RaiseEvent(Click(1, Mouse.MouseUpEvent));

        Assert.True(cell.IsEditing);
    }

    [StaFact]
    public void KeyboardStillOpensTheEditorWithDoubleTap()
    {
        using var f = new Fixture(_roots);
        f.Grid.CurrentCell = new DataGridCellInfo(_plate, f.NameColumn);
        var cell = f.Cell(_plate, f.NameColumn);

        f.Press(cell, Key.F2);

        Assert.True(cell.IsEditing);
    }

    [StaFact]
    public void EnterCommitsAndRaisesCurrentCellEndEditWithTheNewValue()
    {
        using var f = new Fixture(_roots);
        f.Grid.CurrentCell = new DataGridCellInfo(_plate, f.NameColumn);
        f.Grid.BeginEdit();
        f.Pump();
        var editor = f.Editor();
        editor.Text = "Base plate";

        f.Press(editor, Key.Enter);

        Assert.Equal("Base plate", _plate.Name);
        Assert.Equal(["End Base plate"], f.Log);
        Assert.False(f.Grid.SelectionController.CurrentCellManager.CurrentCell?.IsEditing ?? false);
    }

    [StaFact]
    public void EscapeRestoresTheValueAndStillRaisesCurrentCellEndEdit()
    {
        using var f = new Fixture(_roots);
        f.Grid.CurrentCell = new DataGridCellInfo(_plate, f.NameColumn);
        f.Grid.BeginEdit();
        f.Pump();
        var editor = f.Editor();
        editor.Text = "typo";

        f.Press(editor, Key.Escape);

        Assert.Equal("Plate", _plate.Name);
        Assert.Equal(["End Plate"], f.Log);
    }

    [StaFact]
    public void MovingToAnotherCellCommitsTheOpenEditor()
    {
        // The path a click on another cell takes.
        using var f = new Fixture(_roots);
        f.Grid.CurrentCell = new DataGridCellInfo(_plate, f.NameColumn);
        f.Grid.BeginEdit();
        f.Pump();
        f.Editor().Text = "Base plate";

        f.Grid.CurrentCell = new DataGridCellInfo(_frame, f.NameColumn);
        f.Pump();

        Assert.Equal("Base plate", _plate.Name);
        Assert.Equal(["End Base plate"], f.Log);
    }

    [StaFact]
    public void AGetOnlyPropertyDoesNotOpenAnEditor()
    {
        using var f = new Fixture(_roots);
        var began = new List<string>();
        f.Grid.CurrentCellBeginEdit += (_, e) => began.Add(e.Column.SortMemberPath);
        f.Grid.CurrentCell = new DataGridCellInfo(_plate, f.Grid.Columns[1]);

        Assert.False(f.Grid.SelectionController.CurrentCellManager.BeginEdit());
        Assert.Empty(began);
    }

    [StaFact]
    public void ExpandingRightAfterACommittedCellDoesNotThrow()
    {
        // Tab commits the cell only; DataGrid keeps the row's edit open. The rows must still be
        // free to change.
        using var f = new Fixture(_roots);
        f.Grid.CurrentCell = new DataGridCellInfo(_frame, f.NameColumn);
        f.Grid.BeginEdit();
        f.Pump();
        var editor = f.Editor();
        editor.Text = "Frame 2";
        f.Press(editor, Key.Tab);

        f.Grid.ExpandNode(f.Grid.GetNode(_frame)!);
        f.Grid.SortBy(f.NameColumn, System.ComponentModel.ListSortDirection.Descending);
        f.Pump();

        Assert.Equal("Frame 2", _frame.Name);
        Assert.Equal(["Plate", "Frame 2", "Bolt"], f.Grid.Items.Cast<Part>().Select(p => p.Name));
    }

    [StaFact]
    public void AnOpenEditorShowsWhiteInsteadOfTheSelection()
    {
        using var f = new Fixture(_roots);
        f.Grid.SelectCell(_plate, f.NameColumn);
        f.Grid.SelectionController.CurrentCellManager.BeginEdit();
        f.Pump();

        var cell = f.Cell(_plate, f.NameColumn);
        Assert.True(cell.IsSelected);
        Assert.Equal(Colors.White, ((SolidColorBrush)cell.Background).Color);
    }

    [StaFact]
    public void SelectCellsSelectsWholeRowsInRowMode()
    {
        using var f = new Fixture(_roots, g => g.SelectionUnit = DataGridSelectionUnit.FullRow);

        f.Grid.SelectCells(_frame, f.NameColumn, _plate, f.NameColumn);

        Assert.Equal([_frame, _plate], f.Grid.SelectedItems.Cast<Part>());
    }

    [StaFact]
    public void CurrentCellInfoMovesTheCurrentCell()
    {
        using var f = new Fixture(_roots);

        f.Grid.CurrentCellInfo = new TreeGridCellInfo(f.Grid.Columns[1], _plate);

        Assert.Same(_plate, f.Grid.CurrentCell.Item);
        Assert.Same(f.Grid.Columns[1], f.Grid.CurrentCell.Column);
        Assert.Equal(new RowColumnIndex(2, 1), f.Grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex);
    }

    [StaFact]
    public void BeginEditWithoutACurrentCellDoesNothing()
    {
        using var f = new Fixture(_roots);

        Assert.False(f.Grid.SelectionController.CurrentCellManager.BeginEdit());
        Assert.False(f.Grid.SelectionController.CurrentCellManager.HasCurrentCell);
        Assert.Equal(RowColumnIndex.Empty, f.Grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex);
    }
}
