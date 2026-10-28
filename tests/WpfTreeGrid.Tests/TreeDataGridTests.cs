using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace WpfTreeGrid.Tests;

public sealed class Part(string name, double mass, params Part[] children) : INotifyPropertyChanged
{
    private string _name = name;
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
    }

    public double Mass { get; } = mass;

    public bool IsSelected { get; set; }

    public ObservableCollection<Part> Children { get; } = new(children);

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
    }

    public override string ToString() => Name;
}

public sealed class Assembly(string name, params Assembly[] kids)
{
    public string Name { get; } = name;

    public List<Assembly> Kids { get; } = [.. kids];

    public override string ToString() => Name;
}

public sealed record Occurrence(int Id, int ParentId, string Name)
{
    public override string ToString() => Name;
}

public class TreeDataGridTests
{
    // Frame (10)       Plate (8)
    //   Bolt (1)         Nut (0.5)
    //   Beam (20)
    private readonly Part _bolt = new("Bolt", 1);
    private readonly Part _beam = new("Beam", 20);
    private readonly Part _nut = new("Nut", 0.5);
    private readonly Part _frame;
    private readonly Part _plate;
    private readonly ObservableCollection<Part> _roots;

    public TreeDataGridTests()
    {
        _frame = new Part("Frame", 10, _bolt, _beam);
        _plate = new Part("Plate", 8, _nut);
        _roots = [_frame, _plate];
    }

    private static string Rows(TreeDataGrid grid) => string.Join(",", grid.Items.Cast<object>());

    private TreeDataGrid CreateGrid(AutoExpandMode expand = AutoExpandMode.None)
    {
        var grid = new TreeDataGrid { AutoExpandMode = expand, AllowEditing = true };
        grid.Columns.Add(new TreeGridTextColumn { MappingName = nameof(Part.Name) });
        grid.Columns.Add(new TreeGridNumericColumn { MappingName = nameof(Part.Mass), HeaderText = "Mass (kg)" });
        grid.Columns.Add(new TreeGridCheckBoxColumn { MappingName = nameof(Part.IsSelected), AllowFiltering = false });
        grid.ItemsSource = _roots;
        return grid;
    }

    private static Window Show(TreeDataGrid grid)
    {
        var window = new Window { Content = grid, Width = 600, Height = 400, Left = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        // Let deferred template bindings (RelativeSource lookups) settle before asserting on visuals.
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        grid.UpdateLayout();
        return window;
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

    private static FilterPredicate P(FilterType type, object? value, PredicateType join = PredicateType.And) =>
        new() { FilterType = type, FilterValue = value, PredicateType = join };

    [StaFact]
    public void BoundItemsAreReplacedByTheModelRows()
    {
        var grid = CreateGrid();

        Assert.Equal("Frame,Plate", Rows(grid));
        Assert.IsType<TreeRows>(grid.GetValue(ItemsControl.ItemsSourceProperty));
        Assert.Same(_roots, grid.ItemsSource);
        Assert.Same(_roots, grid.View.ItemsSource);

        grid.ExpandNode(grid.View.Nodes[0]);
        Assert.Equal("Frame,Bolt,Beam,Plate", Rows(grid));
    }

    [StaFact]
    public void ItemsSourceBindingIsFollowed()
    {
        var grid = new TreeDataGrid { DataContext = new { Items = _roots } };
        grid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Items"));

        _roots.Add(new Part("Shaft", 3));

        Assert.Equal("Frame,Plate,Shaft", Rows(grid));
    }

    [StaFact]
    public void ChangingChildPropertyNameRebuildsTheTree()
    {
        var grid = new TreeDataGrid { AutoExpandMode = AutoExpandMode.AllNodesExpanded, ItemsSource = new[] { new Assembly("Top", new Assembly("Sub")) } };
        Assert.Equal("Top", Rows(grid));

        grid.ChildPropertyName = nameof(Assembly.Kids);

        Assert.Equal("Top,Sub", Rows(grid));
    }

    [StaFact]
    public void SelfRelationalSourceFollowsTheFlatList()
    {
        var rows = new ObservableCollection<Occurrence> { new(1, -1, "Top"), new(2, 1, "Sub"), new(3, -1, "Other") };
        var grid = new TreeDataGrid
        {
            ChildPropertyName = nameof(Occurrence.Id),
            ParentPropertyName = nameof(Occurrence.ParentId),
            SelfRelationRootValue = "-1",
            AutoExpandMode = AutoExpandMode.AllNodesExpanded,
            ItemsSource = rows,
        };
        Assert.Equal("Top,Sub,Other", Rows(grid));

        rows.Add(new Occurrence(4, 3, "Leaf"));
        grid.ExpandAllNodes();

        Assert.Equal("Top,Sub,Other,Leaf", Rows(grid));
    }

    [StaFact]
    public void ExpandStateMappingNameWritesBackOnlyWhenSet()
    {
        var grid = CreateGrid();
        grid.ExpandNode(grid.GetNode(_frame)!);
        Assert.False(_frame.IsExpanded);

        grid.ExpandStateMappingName = nameof(Part.IsExpanded);
        grid.ExpandNode(grid.GetNode(_plate)!);
        Assert.True(_plate.IsExpanded);
    }

    [StaFact]
    public void NodeExpandingCanCancel()
    {
        var grid = CreateGrid();
        var events = new List<string>();
        grid.NodeExpanding += (_, e) => e.Cancel = e.Node.Item == _plate;
        grid.NodeExpanded += (_, e) => events.Add("expanded " + e.Node.Item);
        grid.NodeCollapsed += (_, e) => events.Add("collapsed " + e.Node.Item);

        grid.ExpandNode(grid.GetNode(_frame)!);
        grid.ExpandNode(grid.GetNode(_plate)!);
        grid.CollapseNode(grid.GetNode(_frame)!);
        grid.ExpandAllNodes(); // bulk changes raise no events

        Assert.Equal(["expanded Frame", "collapsed Frame"], events);
        Assert.Equal("Frame,Bolt,Beam,Plate,Nut", Rows(grid));
    }

    [StaFact]
    public void SortByOrdersSiblingsAndSetsTheGlyph()
    {
        var grid = CreateGrid(AutoExpandMode.AllNodesExpanded);
        var mass = grid.Columns[1];

        grid.SortBy(mass, ListSortDirection.Descending);

        Assert.Equal("Frame,Beam,Bolt,Plate,Nut", Rows(grid));
        Assert.Equal(ListSortDirection.Descending, mass.SortDirection);

        grid.ClearSort();
        Assert.Equal("Frame,Bolt,Beam,Plate,Nut", Rows(grid));
        Assert.Null(mass.SortDirection);
    }

    [StaFact]
    public void FilterPredicatesOnAColumnFilterAtTheDefaultAllLevel()
    {
        var grid = CreateGrid(AutoExpandMode.AllNodesExpanded);
        var mass = (TreeGridNumericColumn)grid.Columns[1];

        mass.FilterPredicates.Add(P(FilterType.GreaterThan, 5.0));

        // All: Frame and Plate match; Bolt and Nut don't; Beam matches under a visible parent.
        Assert.Equal("Frame,Beam,Plate", Rows(grid));
        Assert.True(TreeDataGrid.GetIsColumnFiltered(mass));

        grid.ClearFilters();
        Assert.False(TreeDataGrid.GetIsColumnFiltered(mass));
        Assert.Equal("Frame,Bolt,Beam,Plate,Nut", Rows(grid));
    }

    [StaFact]
    public void ExtendedLevelKeepsAncestorsAndViewFilterCombinesWithColumns()
    {
        var grid = CreateGrid();
        grid.FilterLevel = FilterLevel.Extended;
        grid.ExpandFilterMatches = true;
        grid.Columns[1].SetValue(TreeDataGrid.CanUserFilterProperty, true);
        TreeDataGrid.GetFilterPredicates(grid.Columns[1]).Add(P(FilterType.LessThan, 5.0));

        grid.View.Filter = item => ((Part)item).Name.StartsWith('N');

        Assert.Equal("Plate,Nut", Rows(grid));
    }

    [StaFact]
    public void ColumnsTakeTreeGridSettings()
    {
        var grid = CreateGrid();
        var name = (TreeGridTextColumn)grid.Columns[0];
        var mass = (TreeGridNumericColumn)grid.Columns[1];

        Assert.Equal("Name", name.HeaderText);
        Assert.Equal("Mass (kg)", mass.HeaderText);
        Assert.Equal("Mass", mass.SortMemberPath);
        Assert.Equal(TextAlignment.Right, mass.TextAlignment);
        Assert.Equal("N2", ((Binding)mass.Binding).StringFormat);

        mass.NumberDecimalDigits = 0;
        mass.IsHidden = true;
        mass.ColumnSizer = TreeColumnSizer.Star;
        name.AllowEditing = false;

        Assert.Equal("N0", ((Binding)mass.Binding).StringFormat);
        Assert.Equal(Visibility.Collapsed, mass.Visibility);
        Assert.True(((DataGridColumn)mass).Width.IsStar);
        Assert.True(name.IsReadOnly);
    }

    [StaFact]
    public void GridDefaults()
    {
        var grid = new TreeDataGrid();

        Assert.False(grid.AllowEditing);
        Assert.True(grid.AllowSorting);
        Assert.False(grid.AllowResizingColumns);
        Assert.False(grid.AllowDraggingColumns);
        Assert.False(grid.ShowRowHeader);
        Assert.Equal(DataGridSelectionMode.Single, grid.SelectionMode);
        Assert.Equal(24, grid.RowHeight);
        Assert.Equal(24, grid.HeaderRowHeight);
        Assert.Equal(FilterLevel.All, grid.FilterLevel);
        Assert.Null(grid.ExpandStateMappingName);
    }

    [StaFact]
    public void TreeGridXamlParses()
    {
        const string xaml = """
            <tg:TreeDataGrid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:tg="clr-namespace:WpfTreeGrid;assembly=WpfTreeGrid"
                             AutoGenerateColumns="False" AllowFiltering="True" AllowSorting="True"
                             AllowResizingColumns="True" ShowRowHeader="False" SelectionMode="Extended"
                             AutoExpandMode="RootNodesExpanded" AllowEditing="True" FilterLevel="All"
                             ChildPropertyName="Children" ExpandStateMappingName="IsExpanded"
                             HeaderRowHeight="36" RowHeight="24" ColumnSizer="Star" GridLinesVisibility="Horizontal">
                <tg:TreeDataGrid.Columns>
                    <tg:TreeGridTextColumn MappingName="Name" HeaderText="Name" Width="200" AllowEditing="True" ColumnSizer="Star" />
                    <tg:TreeGridNumericColumn MappingName="Mass" HeaderText="Mass" NumberDecimalDigits="3" TextAlignment="Right" />
                    <tg:TreeGridCheckBoxColumn MappingName="IsSelected" HeaderText="Use" AllowFiltering="False" />
                    <tg:TreeGridTemplateColumn MappingName="Name" HeaderText="Template" MinimumWidth="40" IsHidden="True" AllowSorting="False">
                        <tg:TreeGridTemplateColumn.CellTemplate>
                            <DataTemplate><TextBlock Text="{Binding Name}" /></DataTemplate>
                        </tg:TreeGridTemplateColumn.CellTemplate>
                        <tg:TreeGridTemplateColumn.EditTemplate>
                            <DataTemplate><TextBox Text="{Binding Name}" /></DataTemplate>
                        </tg:TreeGridTemplateColumn.EditTemplate>
                    </tg:TreeGridTemplateColumn>
                </tg:TreeDataGrid.Columns>
            </tg:TreeDataGrid>
            """;

        var grid = (TreeDataGrid)XamlReader.Parse(xaml);
        grid.ItemsSource = _roots;

        Assert.Equal(4, grid.Columns.Count);
        Assert.Equal(DataGridSelectionMode.Extended, grid.SelectionMode);
        Assert.True(grid.AllowEditing);
        Assert.Equal(36, grid.HeaderRowHeight);
        Assert.Equal("Frame,Bolt,Beam,Plate,Nut", Rows(grid));
    }

    [StaFact]
    public void ExpanderGoesInTheFirstColumnOrTheNamedOne()
    {
        var grid = CreateGrid(AutoExpandMode.AllNodesExpanded);
        var window = Show(grid);

        bool HasTreeCell(int column) => Descendants<DataGridCell>(grid).Where(c => c.Column == grid.Columns[column]).All(c => Descendants<TreeCell>(c).Any());

        Assert.True(HasTreeCell(0));
        Assert.False(HasTreeCell(1));

        grid.ExpanderColumn = "Mass";
        grid.UpdateLayout();
        Assert.False(HasTreeCell(0));
        Assert.True(HasTreeCell(1));
        window.Close();
    }

    [StaFact]
    public void TreeCellRendersIndentAndExpanderPerNode()
    {
        var grid = CreateGrid(AutoExpandMode.AllNodesExpanded);
        var window = Show(grid);

        var cells = Descendants<TreeCell>(grid).ToDictionary(c => ((Part)c.DataContext).Name);
        var expanders = cells.ToDictionary(c => c.Key, c => Descendants<ToggleButton>(c.Value).Single());

        Assert.Equal(1, cells["Bolt"].Node!.Level);
        Assert.Equal(Visibility.Visible, expanders["Frame"].Visibility);
        Assert.Equal(Visibility.Hidden, expanders["Bolt"].Visibility);
        double TextX(string name) => Descendants<TextBlock>(cells[name]).Single().TranslatePoint(default, grid).X;
        Assert.Equal(grid.Indent, TextX("Bolt") - TextX("Frame"), precision: 3);

        expanders["Frame"].IsChecked = false;
        Assert.Equal("Frame,Plate,Nut", Rows(grid));
        window.Close();
    }

    [StaFact]
    public void EditingTheExpanderColumnCommitsToTheItem()
    {
        var grid = CreateGrid();
        var window = Show(grid);

        grid.CurrentCell = new DataGridCellInfo(_plate, grid.Columns[0]);
        grid.BeginEdit();
        grid.UpdateLayout();
        var editor = Descendants<TextBox>(grid).Single();
        editor.Text = "Base plate";
        grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        Assert.Equal("Base plate", _plate.Name);
        window.Close();
    }

    [StaFact]
    public void FilterButtonsGoOnFilterableColumnsOnly()
    {
        var grid = CreateGrid();
        grid.AllowFiltering = true;
        var window = Show(grid);

        Assert.True(TreeDataGrid.GetShowsFilterButton(grid.Columns[0]));
        Assert.True(TreeDataGrid.GetShowsFilterButton(grid.Columns[1]));
        Assert.False(TreeDataGrid.GetShowsFilterButton(grid.Columns[2]));
        var buttons = Descendants<DataGridColumnHeader>(grid)
            .Where(h => h.Column is not null)
            .ToDictionary(h => h.Column, h => Descendants<Button>(h).Single().Visibility);
        Assert.Equal(Visibility.Visible, buttons[grid.Columns[0]]);
        Assert.Equal(Visibility.Collapsed, buttons[grid.Columns[2]]);
        window.Close();
    }

    [StaFact]
    public void RecordContextMenuGoesOnRowsOnly()
    {
        var grid = CreateGrid();
        var menu = new ContextMenu();
        grid.RecordContextMenu = menu;
        var window = Show(grid);

        Assert.All(Descendants<DataGridRow>(grid), row => Assert.Same(menu, row.ContextMenu));
        Assert.Null(grid.ContextMenu);
        window.Close();
    }

    [StaFact]
    public void BindableSelectedItemsSyncsBothWays()
    {
        var grid = CreateGrid();
        grid.SelectionMode = DataGridSelectionMode.Extended;
        var selection = new ObservableCollection<object>();
        grid.BindableSelectedItems = selection;

        grid.SelectedItems.Add(_frame);
        Assert.Equal([_frame], selection);

        selection.Add(_plate);
        Assert.Equal(2, grid.SelectedItems.Count);

        selection.Clear();
        Assert.Empty(grid.SelectedItems);
    }

    [StaFact]
    public void FilterPanelRoundTripsConditionsAndChecklists()
    {
        FilterPredicate[] conditions = [P(FilterType.Contains, "o"), P(FilterType.NotEquals, null, PredicateType.Or)];
        var fromConditions = new ColumnFilterPanel(["Beam", "Bolt", "Nut"], conditions).BuildPredicates();
        Assert.Equal(["contains 'o'", "is not empty"], fromConditions.Select(p => p.ToString()));
        Assert.Equal(PredicateType.Or, fromConditions[1].PredicateType);

        FilterPredicate[] checklist = [P(FilterType.Equals, 1.0), P(FilterType.Equals, null, PredicateType.Or)];
        var fromChecklist = new ColumnFilterPanel([null, 1.0, 2.0], checklist).BuildPredicates();
        Assert.Equal([null, "", 1.0], fromChecklist.Select(p => p.FilterValue));
        Assert.Equal(FilterBehavior.StronglyTyped, fromChecklist[2].FilterBehavior);

        Assert.Empty(new ColumnFilterPanel(["Beam"], []).BuildPredicates());
    }
}
