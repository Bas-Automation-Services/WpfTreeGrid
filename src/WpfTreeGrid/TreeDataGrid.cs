using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace WpfTreeGrid;

/// <summary>
/// A <see cref="DataGrid"/> that shows hierarchical data, with tree-grid names over DataGrid's
/// own properties (see <c>TreeDataGrid.Aliases.cs</c>).
/// Bind <see cref="ItemsControl.ItemsSource"/> to the top-level items; the grid walks
/// <see cref="ChildPropertyName"/> and shows the visible rows of its <see cref="View"/>.
/// The expander goes in <see cref="ExpanderColumn"/>, or the first column, when that is one
/// of the <c>TreeGrid*Column</c> types.
///
/// <para>Because the rows are the bound items themselves, columns bind as in any DataGrid
/// (<c>{Binding Name}</c>). <c>ItemsSource</c> reads back as <see cref="TreeModel.Rows"/>.</para>
///
/// <para>Sorting and filtering run through the tree model, never the collection view, so
/// children stay under their parent. Both key off <see cref="DataGridColumn.SortMemberPath"/>
/// (a column's <c>MappingName</c>).</para>
/// </summary>
public partial class TreeDataGrid : DataGrid
{
    public static readonly RoutedCommand ExpandAllCommand = new(nameof(ExpandAllCommand), typeof(TreeDataGrid));
    public static readonly RoutedCommand CollapseAllCommand = new(nameof(CollapseAllCommand), typeof(TreeDataGrid));
    public static readonly RoutedCommand ExpandSelectedCommand = new(nameof(ExpandSelectedCommand), typeof(TreeDataGrid));
    public static readonly RoutedCommand CollapseSelectedCommand = new(nameof(CollapseSelectedCommand), typeof(TreeDataGrid));
    public static readonly RoutedCommand ShowColumnFilterCommand = new(nameof(ShowColumnFilterCommand), typeof(TreeDataGrid));
    public static readonly RoutedCommand ClearFiltersCommand = new(nameof(ClearFiltersCommand), typeof(TreeDataGrid));

    // Resource keys of the default look (Themes/Generic.xaml). Define a resource under the same key in
    // app resources to replace it, or base a style on it via BasedOn and x:Static.
    public static readonly ComponentResourceKey ColumnHeaderStyleKey = new(typeof(TreeDataGrid), "ColumnHeaderStyle");
    public static readonly ComponentResourceKey CellStyleKey = new(typeof(TreeDataGrid), "CellStyle");
    public static readonly ComponentResourceKey ExpanderStyleKey = new(typeof(TreeDataGrid), "ExpanderStyle");
    public static readonly ComponentResourceKey LineBrushKey = new(typeof(TreeDataGrid), "LineBrush");
    public static readonly ComponentResourceKey SelectionBrushKey = new(typeof(TreeDataGrid), "SelectionBrush");
    public static readonly ComponentResourceKey FilteredBrushKey = new(typeof(TreeDataGrid), "FilteredBrush");

    public static readonly DependencyProperty ChildPropertyNameProperty =
        DependencyProperty.Register(nameof(ChildPropertyName), typeof(string), typeof(TreeDataGrid),
            new PropertyMetadata("Children", (d, _) => ((TreeDataGrid)d).RebuildModel()));

    public static readonly DependencyProperty ParentPropertyNameProperty =
        DependencyProperty.Register(nameof(ParentPropertyName), typeof(string), typeof(TreeDataGrid),
            new PropertyMetadata(null, (d, _) => ((TreeDataGrid)d).RebuildModel()));

    public static readonly DependencyProperty SelfRelationRootValueProperty =
        DependencyProperty.Register(nameof(SelfRelationRootValue), typeof(object), typeof(TreeDataGrid),
            new PropertyMetadata(null, (d, _) => ((TreeDataGrid)d).RebuildModel()));

    public static readonly DependencyProperty ExpandStateMappingNameProperty =
        DependencyProperty.Register(nameof(ExpandStateMappingName), typeof(string), typeof(TreeDataGrid),
            new PropertyMetadata(null, (d, _) => ((TreeDataGrid)d).RebuildModel()));

    public static readonly DependencyProperty AutoExpandModeProperty =
        DependencyProperty.Register(nameof(AutoExpandMode), typeof(AutoExpandMode), typeof(TreeDataGrid),
            new PropertyMetadata(AutoExpandMode.None, (d, _) => ((TreeDataGrid)d).RebuildModel()));

    public static readonly DependencyProperty ExpanderColumnProperty =
        DependencyProperty.Register(nameof(ExpanderColumn), typeof(string), typeof(TreeDataGrid),
            new PropertyMetadata(null, (d, _) => ((TreeDataGrid)d).RefreshExpanderColumn()));

    public static readonly DependencyProperty IsFlatProperty =
        DependencyProperty.Register(nameof(IsFlat), typeof(bool), typeof(TreeDataGrid),
            new PropertyMetadata(false, (d, e) => ((TreeDataGrid)d).View.IsFlat = (bool)e.NewValue));

    public static readonly DependencyProperty FilterLevelProperty =
        DependencyProperty.Register(nameof(FilterLevel), typeof(FilterLevel), typeof(TreeDataGrid),
            new PropertyMetadata(FilterLevel.All, (d, e) => ((TreeDataGrid)d).View.FilterLevel = (FilterLevel)e.NewValue));

    public static readonly DependencyProperty ExpandFilterMatchesProperty =
        DependencyProperty.Register(nameof(ExpandFilterMatches), typeof(bool), typeof(TreeDataGrid),
            new PropertyMetadata(false, (d, e) => ((TreeDataGrid)d).View.ExpandFilterMatches = (bool)e.NewValue));

    public static readonly DependencyProperty RowFilterProperty =
        DependencyProperty.Register(nameof(RowFilter), typeof(Predicate<object>), typeof(TreeDataGrid),
            new PropertyMetadata(null, (d, e) => ((TreeDataGrid)d).View.Filter = (Predicate<object>?)e.NewValue));

    public static readonly DependencyProperty IndentProperty =
        DependencyProperty.Register(nameof(Indent), typeof(double), typeof(TreeDataGrid), new PropertyMetadata(14.0));

    public static readonly DependencyProperty AllowFilteringProperty =
        DependencyProperty.Register(nameof(AllowFiltering), typeof(bool), typeof(TreeDataGrid),
            new PropertyMetadata(false, (d, _) => ((TreeDataGrid)d).ApplyFilterHeaders()));

    public static readonly DependencyProperty RecordContextMenuProperty =
        DependencyProperty.Register(nameof(RecordContextMenu), typeof(ContextMenu), typeof(TreeDataGrid),
            new PropertyMetadata(null, (d, _) => ((TreeDataGrid)d).ApplyRecordContextMenu()));

    public static readonly DependencyProperty BindableSelectedItemsProperty =
        DependencyProperty.Register(nameof(BindableSelectedItems), typeof(IList), typeof(TreeDataGrid),
            new PropertyMetadata(null, OnBindableSelectedItemsChanged));

    public static readonly DependencyProperty CanUserFilterProperty =
        DependencyProperty.RegisterAttached("CanUserFilter", typeof(bool), typeof(TreeDataGrid), new PropertyMetadata(true));

    private static readonly DependencyPropertyKey IsColumnFilteredPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("IsColumnFiltered", typeof(bool), typeof(TreeDataGrid), new PropertyMetadata(false));

    public static readonly DependencyProperty IsColumnFilteredProperty = IsColumnFilteredPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ShowsFilterButtonPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("ShowsFilterButton", typeof(bool), typeof(TreeDataGrid), new PropertyMetadata(false));

    /// <summary>Set on each column: whether its header shows the filter button (AllowFiltering, the column's CanUserFilter and a MappingName).</summary>
    public static readonly DependencyProperty ShowsFilterButtonProperty = ShowsFilterButtonPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey FilterPredicatesPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("FilterPredicates", typeof(ObservableCollection<FilterPredicate>), typeof(TreeDataGrid), new PropertyMetadata(null));

    public static readonly DependencyProperty FilterPredicatesProperty = FilterPredicatesPropertyKey.DependencyProperty;

    private readonly HashSet<DataGridColumn> _observedColumns = [];
    private IEnumerable? _source;
    private SelfRelation? _selfRelation;
    private bool _syncingSelection;
    private bool _batchingFilters;
    private bool _batchingSort;
    private List<object>? _preservedSelection;
    private List<DataGridCellInfo>? _preservedCells;
    private DataGridCellInfo? _preservedCurrentCell;

    public TreeDataGrid()
    {
        View = CreateModel();
        Columns.CollectionChanged += OnColumnsChanged;
        SortColumnDescriptions.CollectionChanged += (_, _) => ApplySort();
        Sorting += OnOwnSorting;
        AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnCellMouseLeftButtonUp), handledEventsToo: true);

        // The default look; resource references, so XAML settings and app resources override them.
        SetResourceReference(ColumnHeaderStyleProperty, ColumnHeaderStyleKey);
        SetResourceReference(CellStyleProperty, CellStyleKey);
        SetResourceReference(HorizontalGridLinesBrushProperty, LineBrushKey);
        SetResourceReference(VerticalGridLinesBrushProperty, LineBrushKey);
        // Format numbers and dates in the user's culture instead of WPF's en-US default.
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);

        CommandBindings.Add(new CommandBinding(ExpandAllCommand, (_, e) => View.ExpandAll(NodeOf(e.Parameter))));
        CommandBindings.Add(new CommandBinding(CollapseAllCommand, (_, e) => View.CollapseAll(NodeOf(e.Parameter))));
        CommandBindings.Add(new CommandBinding(ExpandSelectedCommand, (_, _) => ExpandSelectedNodes()));
        CommandBindings.Add(new CommandBinding(CollapseSelectedCommand, (_, _) => CollapseSelectedNodes()));
        CommandBindings.Add(new CommandBinding(ShowColumnFilterCommand,
            (_, e) => ShowColumnFilter((DataGridColumn)e.Parameter, (UIElement)e.OriginalSource),
            (_, e) => e.CanExecute = e.Parameter is DataGridColumn));
        CommandBindings.Add(new CommandBinding(ClearFiltersCommand,
            (_, _) =>
            {
                ClearFilters();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            },
            (_, e) => e.CanExecute = Columns.Any(c => GetFilterPredicates(c).Count > 0)));
    }

    /// <summary>Raised after the user changes a column filter. Not for programmatic changes.</summary>
    public event EventHandler? FilterChanged;

    /// <summary>Raised before a node expands; set Cancel to keep it collapsed.</summary>
    public event EventHandler<NodeExpandingEventArgs>? NodeExpanding;

    public event EventHandler<NodeExpandedEventArgs>? NodeExpanded;

    /// <summary>Raised before a node collapses; set Cancel to keep it expanded.</summary>
    public event EventHandler<NodeCollapsingEventArgs>? NodeCollapsing;

    public event EventHandler<NodeCollapsedEventArgs>? NodeCollapsed;

    /// <summary>
    /// The tree behind the rows, with the top-level nodes in <c>View.Nodes</c>. Replaced when
    /// <see cref="ChildPropertyName"/>, <see cref="ExpandStateMappingName"/>, <see cref="AutoExpandMode"/>
    /// or the self-relation settings change.
    /// </summary>
    public TreeModel View { get; private set; }

    /// <summary>
    /// The bound top-level collection. (The DataGrid underneath shows
    /// <see cref="TreeModel.Rows"/>; read <c>GetValue(ItemsSourceProperty)</c> for those.)
    /// </summary>
    public new IEnumerable? ItemsSource
    {
        get => _source;
        set => base.ItemsSource = value;
    }

    /// <summary>
    /// Property holding each item's child collection; items without it are leaves. In
    /// self-relational mode (<see cref="ParentPropertyName"/> set) it is the key property instead.
    /// </summary>
    public string ChildPropertyName
    {
        get => (string)GetValue(ChildPropertyNameProperty);
        set => SetValue(ChildPropertyNameProperty, value);
    }

    /// <summary>
    /// Switches to self-relational mode: ItemsSource is a flat list, and each item's
    /// <see cref="ParentPropertyName"/> holds the <see cref="ChildPropertyName"/> key of its parent.
    /// </summary>
    public string? ParentPropertyName
    {
        get => (string?)GetValue(ParentPropertyNameProperty);
        set => SetValue(ParentPropertyNameProperty, value);
    }

    /// <summary>Parent key that marks a top-level item in self-relational mode (null by default).</summary>
    public object? SelfRelationRootValue
    {
        get => GetValue(SelfRelationRootValueProperty);
        set => SetValue(SelfRelationRootValueProperty, value);
    }

    /// <summary>Bool property the expand state is read from and written back to; null or empty keeps it on the grid only.</summary>
    public string? ExpandStateMappingName
    {
        get => (string?)GetValue(ExpandStateMappingNameProperty);
        set => SetValue(ExpandStateMappingNameProperty, value);
    }

    /// <summary>Expansion applied when the rows are loaded: on each new ItemsSource, and when this changes.</summary>
    public AutoExpandMode AutoExpandMode
    {
        get => (AutoExpandMode)GetValue(AutoExpandModeProperty);
        set => SetValue(AutoExpandModeProperty, value);
    }

    /// <summary>MappingName of the column that shows the expander; the first column when empty.</summary>
    public string? ExpanderColumn
    {
        get => (string?)GetValue(ExpanderColumnProperty);
        set => SetValue(ExpanderColumnProperty, value);
    }

    /// <summary>Shows every node as a plain list, without indent or expanders.</summary>
    public bool IsFlat
    {
        get => (bool)GetValue(IsFlatProperty);
        set => SetValue(IsFlatProperty, value);
    }

    /// <summary>Which rows the filters apply to. Defaults to <see cref="WpfTreeGrid.FilterLevel.All"/>.</summary>
    public FilterLevel FilterLevel
    {
        get => (FilterLevel)GetValue(FilterLevelProperty);
        set => SetValue(FilterLevelProperty, value);
    }

    /// <summary>Expand the ancestors of filter matches when a filter is applied. Off by default.</summary>
    public bool ExpandFilterMatches
    {
        get => (bool)GetValue(ExpandFilterMatchesProperty);
        set => SetValue(ExpandFilterMatchesProperty, value);
    }

    /// <summary>
    /// Bindable row filter (e.g. for a search box), combined with the column filters. Same as
    /// setting <c>View.Filter</c>.
    /// </summary>
    public Predicate<object>? RowFilter
    {
        get => (Predicate<object>?)GetValue(RowFilterProperty);
        set => SetValue(RowFilterProperty, value);
    }

    /// <summary>Indent per tree level, in pixels.</summary>
    public double Indent
    {
        get => (double)GetValue(IndentProperty);
        set => SetValue(IndentProperty, value);
    }

    /// <summary>
    /// Shows a filter button in the header of every column with a MappingName whose
    /// <c>AllowFiltering</c> (<c>CanUserFilter</c>) is true. The button is part of the default
    /// column header style; a replacement header style should be based on <see cref="ColumnHeaderStyleKey"/>.
    /// </summary>
    public bool AllowFiltering
    {
        get => (bool)GetValue(AllowFilteringProperty);
        set => SetValue(AllowFilteringProperty, value);
    }

    /// <summary>Context menu for data rows only, not the headers or empty space. Its DataContext is the row's item.</summary>
    public ContextMenu? RecordContextMenu
    {
        get => (ContextMenu?)GetValue(RecordContextMenuProperty);
        set => SetValue(RecordContextMenuProperty, value);
    }

    /// <summary>Two-way mirror of <see cref="MultiSelector.SelectedItems"/> for binding to a view-model list.</summary>
    public IList? BindableSelectedItems
    {
        get => (IList?)GetValue(BindableSelectedItemsProperty);
        set => SetValue(BindableSelectedItemsProperty, value);
    }

    public static bool GetCanUserFilter(DataGridColumn column) => (bool)column.GetValue(CanUserFilterProperty);

    public static void SetCanUserFilter(DataGridColumn column, bool value) => column.SetValue(CanUserFilterProperty, value);

    public static bool GetIsColumnFiltered(DataGridColumn column) => (bool)column.GetValue(IsColumnFilteredProperty);

    public static bool GetShowsFilterButton(DataGridColumn column) => (bool)column.GetValue(ShowsFilterButtonProperty);

    /// <summary>
    /// The column's filter, evaluated left to right.
    /// Changes apply immediately. The <c>TreeGrid*Column</c> types expose it as a property.
    /// </summary>
    public static ObservableCollection<FilterPredicate> GetFilterPredicates(DataGridColumn column)
    {
        if (column.GetValue(FilterPredicatesProperty) is ObservableCollection<FilterPredicate> predicates)
            return predicates;
        predicates = [];
        column.SetValue(FilterPredicatesPropertyKey, predicates);
        return predicates;
    }

    public TreeNode? GetNode(object item) => View.GetNode(item);

    public void ExpandNode(TreeNode node) => node.IsExpanded = true;

    public void CollapseNode(TreeNode node) => node.IsExpanded = false;

    public void ExpandAllNodes() => View.ExpandAll();

    /// <summary>Expands <paramref name="node"/> and everything below it.</summary>
    public void ExpandAllNodes(TreeNode node) => View.ExpandAll(node);

    public void CollapseAllNodes() => View.CollapseAll();

    /// <summary>Collapses <paramref name="node"/> and everything below it.</summary>
    public void CollapseAllNodes(TreeNode node) => View.CollapseAll(node);

    /// <summary>Opens the first <paramref name="levels"/> levels and closes the rest.</summary>
    public void ExpandToLevel(int levels) => View.ExpandToLevel(levels);

    /// <summary>
    /// The sort, primary key first. Header clicks edit it;
    /// add, remove or clear descriptions to sort from code.
    /// </summary>
    public ObservableCollection<SortColumnDescription> SortColumnDescriptions { get; } = [];

    /// <summary>Sorts by <paramref name="column"/>; <paramref name="add"/> keeps the existing sort columns as primary keys.</summary>
    public void SortBy(DataGridColumn column, ListSortDirection direction, bool add = false)
    {
        var name = column.SortMemberPath;
        _batchingSort = true;
        try
        {
            if (!add)
                SortColumnDescriptions.Clear();
            if (SortColumnDescriptions.FirstOrDefault(d => d.ColumnName == name) is { } existing)
                SortColumnDescriptions.Remove(existing);
            SortColumnDescriptions.Add(new SortColumnDescription { ColumnName = name, SortDirection = direction });
        }
        finally
        {
            _batchingSort = false;
        }
        ApplySort();
    }

    public void ClearSort() => SortColumnDescriptions.Clear();

    /// <summary>Expands every selected row and everything below it.</summary>
    public void ExpandSelectedNodes() => ForSelectedNodes(View.ExpandAll);

    /// <summary>Collapses every selected row and everything below it.</summary>
    public void CollapseSelectedNodes() => ForSelectedNodes(View.CollapseAll);

    public void ClearFilter(DataGridColumn column) => GetFilterPredicates(column).Clear();

    public void ClearFilters() => BatchFilters(() =>
    {
        foreach (var column in Columns)
            GetFilterPredicates(column).Clear();
    });

    /// <summary>Applies several filter changes as one re-filter.</summary>
    public void BatchFilters(Action changes)
    {
        _batchingFilters = true;
        try
        {
            changes();
        }
        finally
        {
            _batchingFilters = false;
        }
        ApplyColumnFilters();
    }

    internal bool IsExpanderColumn(DataGridColumn? column) =>
        column is not null && (string.IsNullOrEmpty(ExpanderColumn)
            ? Columns.Count > 0 && Columns[0] == column
            : column.SortMemberPath == ExpanderColumn);

    // DataGrid.OnSorting resets Handled before raising Sorting and sorts the collection view unless a
    // handler sets it again. This grid's handler is attached first, so it always does, and sorts the
    // tree instead; handlers added by users still run.
    private void OnOwnSorting(object? sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        SortBy(e.Column, direction, add: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
    {
        base.OnItemsSourceChanged(oldValue, newValue);
        // DataGrid clears the sort glyphs on a new source; the model keeps sorting, so put them back.
        ApplySortGlyphs();
    }

    protected override void OnLoadingRow(DataGridRowEventArgs e)
    {
        base.OnLoadingRow(e);
        if (RecordContextMenu is not null)
            e.Row.ContextMenu = RecordContextMenu;
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (_syncingSelection || BindableSelectedItems is not { } target) return;

        _syncingSelection = true;
        try
        {
            foreach (var item in e.RemovedItems)
                target.Remove(item);
            foreach (var item in e.AddedItems)
                if (!target.Contains(item))
                    target.Add(item);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Only while a cell has focus (not an editor), and only where Left/Right don't mean "next cell".
        if (e.OriginalSource is DataGridCell && CurrentItem is { } item && GetNode(item) is { } node
            && (SelectionUnit == DataGridSelectionUnit.FullRow || IsExpanderColumn(CurrentColumn)))
        {
            e.Handled = HandleTreeKey(e.Key, node);
        }
        if (!e.Handled)
            base.OnKeyDown(e);
    }

    private bool HandleTreeKey(Key key, TreeNode node)
    {
        switch (key)
        {
            case Key.Right or Key.Add when node.ShowsExpander && !node.IsExpanded:
                node.IsExpanded = true;
                return true;
            case Key.Left or Key.Subtract when node.ShowsExpander && node.IsExpanded:
                node.IsExpanded = false;
                return true;
            case Key.Left when !IsFlat && node.ParentNode is { } parent:
                FocusRow(parent.Item);
                return true;
            case Key.Multiply:
                View.ExpandAll(node);
                return true;
            default:
                return false;
        }
    }

    private void FocusRow(object item)
    {
        SelectedItem = item;
        ScrollIntoView(item);
        UpdateLayout();
        CurrentCell = new DataGridCellInfo(item, CurrentColumn ?? Columns.FirstOrDefault());
        if (ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row
            && CurrentColumn?.GetCellContent(row)?.Parent is DataGridCell cell)
            cell.Focus();
    }

    private TreeModel CreateModel()
    {
        var schema = TreeSchema.FromPropertyNames(ChildPropertyName, ExpandStateMappingName);
        _selfRelation = string.IsNullOrEmpty(ParentPropertyName)
            ? null
            : new SelfRelation(ChildPropertyName, ParentPropertyName, SelfRelationRootValue);
        if (_selfRelation is not null)
            schema = schema.WithChildren(_selfRelation.ChildrenOf);

        var model = new TreeModel(schema)
        {
            AutoExpandMode = AutoExpandMode,
            ExpandFilterMatches = ExpandFilterMatches,
            IsFlat = IsFlat,
            FilterLevel = FilterLevel,
            Filter = RowFilter,
        };
        model.NodeExpandStateChanging += OnNodeExpandStateChanging;
        model.NodeExpandStateChanged += OnNodeExpandStateChanged;
        model.Rows.Changing += OnRowsChanging;
        model.Rows.Changed += OnRowsChanged;
        return model;
    }

    private void RebuildModel()
    {
        View = CreateModel();
        ApplySort();
        ApplyColumnFilters();
        SetSource(_source);
        CoerceValue(ItemsSourceProperty);
    }

    private object? CoerceItemsSource(object? value)
    {
        if (value is TreeRows)
            return value;
        if (!ReferenceEquals(_source, value))
            SetSource((IEnumerable?)value);
        return value is null ? null : View.Rows;
    }

    private void SetSource(IEnumerable? source)
    {
        if (_source is INotifyCollectionChanged old)
            CollectionChangedEventManager.RemoveHandler(old, OnFlatSourceChanged);
        _source = source;

        if (_selfRelation is null)
        {
            View.ItemsSource = source;
            return;
        }
        // Self-relational: the model sees the derived roots; flat-list changes rebuild the relation.
        if (source is INotifyCollectionChanged flat)
            CollectionChangedEventManager.AddHandler(flat, OnFlatSourceChanged);
        _selfRelation.Rebuild(source);
        View.ItemsSource = _selfRelation.Roots;
    }

    private void OnFlatSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _selfRelation?.Rebuild(_source);
        View.Refresh();
    }

    // A sort or expand reaches the DataGrid as rows removed and re-inserted, which drops their
    // selection and the current cell. Remember what was selected and put back what is still shown.
    private void OnRowsChanging(object? sender, EventArgs e)
    {
        if (_preservedCurrentCell is null && CurrentCell.IsValid)
            _preservedCurrentCell = CurrentCell;
        if (SelectionUnit != DataGridSelectionUnit.FullRow)
        {
            if (_preservedCells is null && SelectedCells.Count > 0)
                _preservedCells = SelectedCells.Where(c => c.IsValid).ToList();
            return;
        }
        if (_preservedSelection is null && SelectedItems.Count > 0)
            _preservedSelection = SelectedItems.Cast<object>().ToList();
    }

    private void OnRowsChanged(object? sender, EventArgs e)
    {
        if (_preservedCurrentCell is { } current)
        {
            _preservedCurrentCell = null;
            if (!CurrentCell.IsValid && View.Rows.Contains(current.Item))
                CurrentCell = new DataGridCellInfo(current.Item, current.Column);
        }
        if (_preservedCells is { } cells)
        {
            // DataGrid tracks selected cells by row position, so inserted rows shift the selection
            // onto other items: put back exactly the cells that were selected. New cell infos, since
            // the saved ones remember their old row position.
            _preservedCells = null;
            UnselectAllCells();
            foreach (var cell in cells.Where(c => View.Rows.Contains(c.Item)))
                SelectedCells.Add(new DataGridCellInfo(cell.Item, cell.Column));
        }
        if (_preservedSelection is not { } preserved) return;
        _preservedSelection = null;

        var stillShown = preserved.Where(View.Rows.Contains).ToList();
        if (stillShown.Count == SelectedItems.Count && stillShown.All(SelectedItems.Contains)) return;

        if (SelectionMode == DataGridSelectionMode.Single)
        {
            SelectedItem = stillShown.FirstOrDefault();
            return;
        }
        foreach (var item in stillShown.Where(i => !SelectedItems.Contains(i)))
            SelectedItems.Add(item);
    }

    private void OnNodeExpandStateChanging(object? sender, NodeExpandStateChangingEventArgs e)
    {
        if (e.IsExpanding)
        {
            var args = new NodeExpandingEventArgs(e.Node);
            NodeExpanding?.Invoke(this, args);
            e.Cancel = args.Cancel;
        }
        else
        {
            var args = new NodeCollapsingEventArgs(e.Node);
            NodeCollapsing?.Invoke(this, args);
            e.Cancel = args.Cancel;
        }
    }

    private void OnNodeExpandStateChanged(object? sender, NodeExpandStateChangedEventArgs e)
    {
        if (e.IsExpanded)
            NodeExpanded?.Invoke(this, new NodeExpandedEventArgs(e.Node));
        else
            NodeCollapsed?.Invoke(this, new NodeCollapsedEventArgs(e.Node));
    }

    private void ApplySort()
    {
        if (_batchingSort) return;
        View.Sort = SortColumnDescriptions.Count == 0
            ? null
            : ValueComparer.Create(SortColumnDescriptions
                .Select(d => new SortKey(new MemberPath(d.ColumnName ?? ""), d.SortDirection == ListSortDirection.Descending))
                .ToList());
        ApplySortGlyphs();
    }

    private void ApplySortGlyphs()
    {
        foreach (var column in Columns)
            column.SortDirection = SortColumnDescriptions.FirstOrDefault(d => d.ColumnName == column.SortMemberPath)?.SortDirection;
    }

    private void ApplyColumnFilters()
    {
        if (_batchingFilters) return;

        foreach (var column in Columns)
            column.SetValue(IsColumnFilteredPropertyKey, GetFilterPredicates(column).Count > 0);

        var filters = Columns
            .Where(c => !string.IsNullOrEmpty(c.SortMemberPath) && GetFilterPredicates(c).Count > 0)
            .Select(c => (Path: new MemberPath(c.SortMemberPath), Predicates: (IReadOnlyList<FilterPredicate>)GetFilterPredicates(c).ToList()))
            .ToList();
        View.ColumnPredicate = filters.Count == 0
            ? null
            : item => filters.All(f => FilterPredicate.Matches(f.Predicates, f.Path.GetValue(item)));
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var column in _observedColumns.Where(c => !Columns.Contains(c)).ToList())
        {
            GetFilterPredicates(column).CollectionChanged -= OnFilterPredicatesChanged;
            _observedColumns.Remove(column);
        }
        foreach (var column in Columns.Where(_observedColumns.Add))
            GetFilterPredicates(column).CollectionChanged += OnFilterPredicatesChanged;

        foreach (var column in e.NewItems?.OfType<DataGridColumn>() ?? [])
        {
            InheritDataContext(column);
            BaseOnDefaultCellStyle(column.CellStyle);
        }
        ApplySortGlyphs();
        ApplyColumnFilters();
        ApplyFilterHeaders();
    }

    /// <summary>
    /// Columns are not in the visual tree, so a plain <c>{Binding ShowSomething}</c> on one has no
    /// DataContext. Re-point such bindings at the grid's
    /// DataContext so view-model bindings on columns (IsHidden, HeaderText...) work.
    /// </summary>
    private void InheritDataContext(DataGridColumn column)
    {
        var local = column.GetLocalValueEnumerator();
        var rebinds = new List<(DependencyProperty Property, Binding Binding)>();
        while (local.MoveNext())
        {
            if (BindingOperations.GetBinding(column, local.Current.Property) is { Source: null, RelativeSource: null, ElementName: null } binding)
                rebinds.Add((local.Current.Property, binding));
        }
        foreach (var (property, binding) in rebinds)
        {
            var path = string.IsNullOrEmpty(binding.Path?.Path) ? "DataContext" : "DataContext." + binding.Path.Path;
            BindingOperations.SetBinding(column, property, new Binding
            {
                Path = new PropertyPath(path, binding.Path?.PathParameters.ToArray() ?? []),
                Source = this,
                Mode = binding.Mode,
                Converter = binding.Converter,
                ConverterParameter = binding.ConverterParameter,
                ConverterCulture = binding.ConverterCulture,
                StringFormat = binding.StringFormat,
                FallbackValue = binding.FallbackValue,
                TargetNullValue = binding.TargetNullValue,
                UpdateSourceTrigger = binding.UpdateSourceTrigger,
            });
        }
    }

    /// <summary>
    /// A cell style set on the grid or a column replaces the default cell look entirely in WPF;
    /// here it should add to it. Base such a style on the default while it is still unsealed.
    /// </summary>
    internal void BaseOnDefaultCellStyle(Style? style)
    {
        if (style is { IsSealed: false, BasedOn: null } && style.TargetType == typeof(DataGridCell)
            && TryFindResource(CellStyleKey) is Style defaultStyle && !ReferenceEquals(style, defaultStyle))
            style.BasedOn = defaultStyle;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CellStyleProperty)
            BaseOnDefaultCellStyle(e.NewValue as Style);
    }

    private void OnFilterPredicatesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyColumnFilters();

    private void RefreshExpanderColumn()
    {
        if (IsLoaded) Items.Refresh();
    }

    private void ApplyRecordContextMenu()
    {
        for (var i = 0; i < Items.Count; i++)
            if (ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                row.ContextMenu = RecordContextMenu;
    }

    internal void ApplyFilterHeaders()
    {
        foreach (var column in Columns)
            column.SetValue(ShowsFilterButtonPropertyKey, AllowFiltering && GetCanUserFilter(column) && !string.IsNullOrEmpty(column.SortMemberPath));
    }

    private void ShowColumnFilter(DataGridColumn column, UIElement placementTarget)
    {
        var path = new MemberPath(column.SortMemberPath);
        var values = View.AllNodes
            .Select(n => path.GetValue(n.Item))
            .DistinctBy(v => FilterText.Of(v))
            .OrderBy(v => FilterText.Of(v), Comparer<string>.Create(ValueComparer.CompareNatural))
            .ToList();

        var panel = new ColumnFilterPanel(values, GetFilterPredicates(column));
        var popup = FilterPopup = new Popup
        {
            Child = panel,
            PlacementTarget = placementTarget,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        panel.Applied += predicates =>
        {
            popup.IsOpen = false;
            BatchFilters(() =>
            {
                var target = GetFilterPredicates(column);
                target.Clear();
                foreach (var predicate in predicates)
                    target.Add(predicate);
            });
            FilterChanged?.Invoke(this, EventArgs.Empty);
        };
        panel.Cancelled += () => popup.IsOpen = false;
        panel.SortRequested += direction =>
        {
            popup.IsOpen = false;
            SortBy(column, direction);
        };
        popup.Opened += (_, _) => panel.FocusFirstInput();
        popup.IsOpen = true;
    }

    /// <summary>The filter popup last opened, for tests.</summary>
    internal Popup? FilterPopup { get; private set; }

    private TreeNode? NodeOf(object? parameter) => parameter is null ? null : GetNode(parameter);

    private void ForSelectedNodes(Action<TreeNode> action)
    {
        foreach (var node in SelectedItems.Cast<object>().Select(GetNode).OfType<TreeNode>().ToList())
            action(node);
    }

    private static void OnBindableSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (TreeDataGrid)d;
        if (e.OldValue is INotifyCollectionChanged oldList)
            CollectionChangedEventManager.RemoveHandler(oldList, grid.OnBindableSelectedItemsCollectionChanged);
        if (e.NewValue is INotifyCollectionChanged newList)
            CollectionChangedEventManager.AddHandler(newList, grid.OnBindableSelectedItemsCollectionChanged);
        grid.PushSelection();
    }

    private void OnBindableSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => PushSelection();

    private void PushSelection()
    {
        if (_syncingSelection) return;

        _syncingSelection = true;
        try
        {
            // Rows that are collapsed or filtered away can't be selected in the grid.
            var wanted = BindableSelectedItems?.Cast<object>().Where(View.Rows.Contains).ToList() ?? [];
            if (SelectionMode == DataGridSelectionMode.Single)
            {
                SelectedItem = wanted.FirstOrDefault();
                return;
            }
            SelectedItems.Clear();
            foreach (var item in wanted)
                SelectedItems.Add(item);
        }
        finally
        {
            _syncingSelection = false;
        }
    }
}
