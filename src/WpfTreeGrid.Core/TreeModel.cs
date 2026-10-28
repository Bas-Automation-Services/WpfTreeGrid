using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WpfTreeGrid;

public enum AutoExpandMode
{
    /// <summary>Keep the expand state the items report through the schema.</summary>
    None,
    RootNodesExpanded,
    AllNodesExpanded,
}

/// <summary>Which nodes a filter is applied to.</summary>
public enum FilterLevel
{
    /// <summary>Only top-level rows are filtered; everything below a visible row shows.</summary>
    Root,

    /// <summary>Every row must match; a row that fails hides its whole subtree.</summary>
    All,

    /// <summary>Every row is filtered, but the ancestors of a match stay visible even when they don't match.</summary>
    Extended,
}

/// <summary>
/// Turns a hierarchy of bound items into the flat list of visible rows a grid shows
/// in <see cref="Rows"/>, and keeps that list in step with expand/collapse, sorting,
/// filtering and changes to observable child collections.
///
/// <para>Every change recomputes the visible rows in one pass and diffs them against
/// the current list, so there is a single source of truth for what is shown. Items
/// are tracked by reference: the same instance must not appear twice in the tree.</para>
/// </summary>
public sealed class TreeModel
{
    private readonly TreeSchema _schema;
    private readonly Dictionary<object, TreeNode> _nodes = new(ReferenceEqualityComparer.Instance);
    private readonly TreeNode _root;
    private Comparison<object>? _sort;
    private IComparer<object>? _comparer;
    private Predicate<object>? _filter;
    private Predicate<object>? _columnPredicate;
    private bool _isFlat;
    private FilterLevel _filterLevel = FilterLevel.All;

    public TreeModel(TreeSchema schema)
    {
        _schema = schema;
        _root = new TreeNode(this, new object(), null) { IsExpandedCore = true };
    }

    public TreeRows Rows { get; } = new();

    /// <summary>
    /// Raised before a single node expands or collapses; set <see cref="NodeExpandStateChangingEventArgs.Cancel"/>
    /// to keep it as is. Bulk changes (expand/collapse all, auto-expand) raise no events.
    /// </summary>
    public event EventHandler<NodeExpandStateChangingEventArgs>? NodeExpandStateChanging;

    /// <summary>Raised after a node expanded or collapsed, before the rows are updated.</summary>
    public event EventHandler<NodeExpandStateChangedEventArgs>? NodeExpandStateChanged;

    /// <summary>The top-level nodes.</summary>
    public IReadOnlyList<TreeNode> Nodes => _root.ChildList;

    /// <summary>Every node in the tree, depth-first, regardless of expand state or filter.</summary>
    public IEnumerable<TreeNode> AllNodes => _root.Descendants();

    /// <summary>Applied each time <see cref="ItemsSource"/> is set.</summary>
    public AutoExpandMode AutoExpandMode { get; set; }

    /// <summary>When a filter is set, expand the ancestors of matching rows so the matches are on screen. Off by default.</summary>
    public bool ExpandFilterMatches { get; set; }

    public FilterLevel FilterLevel
    {
        get => _filterLevel;
        set
        {
            _filterLevel = value;
            ApplyFilter(_root, expandMatches: false);
            Update();
        }
    }

    /// <summary>The top-level items. Observed when it implements <c>INotifyCollectionChanged</c>.</summary>
    public IEnumerable? ItemsSource
    {
        get => _root.ChildrenSource;
        set
        {
            DetachChildren(_root);
            Attach(_root, value);

            // Every node in range is marked expanded, leaves included, without events.
            if (AutoExpandMode != AutoExpandMode.None)
                foreach (var node in AllNodes.Where(n => AutoExpandMode == AutoExpandMode.AllNodesExpanded || n.Level == 0))
                    SetExpandedCore(node, true, raiseEvents: false);

            ApplyFilter(_root, ExpandFilterMatches && IsFiltered);
            Update();
        }
    }

    /// <summary>Sorts siblings within each parent (globally in flat mode). The sort is stable; null restores source order.</summary>
    public Comparison<object>? Sort
    {
        get => _sort;
        set
        {
            _sort = value;
            _comparer = value is null ? null : Comparer<object>.Create(value);
            RebuildAll();
            Update();
        }
    }

    /// <summary>Rows to keep, applied according to <see cref="FilterLevel"/>. Null shows everything.</summary>
    public Predicate<object>? Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            ApplyFilter(_root, ExpandFilterMatches && value is not null);
            Update();
        }
    }

    /// <summary>
    /// The column filters, kept apart from <see cref="Filter"/> so a view-level filter and the
    /// grid's column filters don't overwrite each other. A row must pass both.
    /// </summary>
    public Predicate<object>? ColumnPredicate
    {
        get => _columnPredicate;
        set
        {
            _columnPredicate = value;
            ApplyFilter(_root, ExpandFilterMatches && value is not null);
            Update();
        }
    }

    /// <summary>Shows every matching node as a plain list, without indentation or expanders.</summary>
    public bool IsFlat
    {
        get => _isFlat;
        set
        {
            if (_isFlat == value) return;
            _isFlat = value;
            foreach (var node in AllNodes)
                node.OnPropertyChanged(nameof(TreeNode.DisplayLevel));
            Update();
        }
    }

    public TreeNode? GetNode(object item) => _nodes.GetValueOrDefault(item);

    /// <summary>
    /// Expands <paramref name="node"/> and everything below it, or the whole tree when null.
    /// Leaves are marked expanded too and no expand events are raised for bulk changes.
    /// </summary>
    public void ExpandAll(TreeNode? node = null)
    {
        foreach (var n in Subtree(node))
            SetExpandedCore(n, true, raiseEvents: false);
        Update();
    }

    /// <summary>
    /// Collapses <paramref name="node"/> and everything below it, or the whole tree when null.
    /// Leaves lose their expanded mark too and no events are raised.
    /// </summary>
    public void CollapseAll(TreeNode? node = null)
    {
        foreach (var n in Subtree(node))
            SetExpandedCore(n, false, raiseEvents: false);
        Update();
    }

    /// <summary>Opens the first <paramref name="levels"/> levels and closes the rest; 1 shows the top-level rows' children.</summary>
    public void ExpandToLevel(int levels)
    {
        foreach (var node in AllNodes)
            SetExpandedCore(node, node.HasChildNodes && node.Level < levels, raiseEvents: false);
        Update();
    }

    /// <summary>
    /// Re-reads every child collection and re-runs sort and filter. Needed after
    /// changes the model cannot observe: non-observable collections, or edits to
    /// properties the sort or filter depend on.
    /// </summary>
    public void Refresh()
    {
        RebuildAll();
        ApplyFilter(_root, expandMatches: false);
        Update();
    }

    /// <summary>Re-runs the filters, e.g. after edits to properties they depend on.</summary>
    public void RefreshFilter()
    {
        ApplyFilter(_root, expandMatches: false);
        Update();
    }

    internal void SetExpanded(TreeNode node, bool value)
    {
        if (node.IsExpandedCore == value) return;
        SetExpandedCore(node, value);
        Update();
    }

    internal void OnChildrenChanged(TreeNode node)
    {
        BuildChildren(node);
        if (IsFiltered)
            ApplyFilter(_root, expandMatches: false);
        Update();
    }

    internal void OnItemPropertyChanged(TreeNode node, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == _schema.IsExpandedPropertyName)
            SetExpanded(node, _schema.GetIsExpanded?.Invoke(node.Item) ?? false);
    }

    private bool IsFiltered => _filter is not null || _columnPredicate is not null;

    private bool PassesFilters(object item) =>
        (_filter?.Invoke(item) ?? true) && (_columnPredicate?.Invoke(item) ?? true);

    private IEnumerable<TreeNode> Subtree(TreeNode? node) => node is null ? AllNodes : node.Descendants().Prepend(node);

    private void SetExpandedCore(TreeNode node, bool value, bool raiseEvents = true)
    {
        if (node.IsExpandedCore == value) return;
        if (raiseEvents && NodeExpandStateChanging is { } changing)
        {
            var args = new NodeExpandStateChangingEventArgs(node, value);
            changing(this, args);
            if (args.Cancel)
            {
                // A bound expander toggle has already flipped; tell it to flip back.
                node.OnPropertyChanged(nameof(TreeNode.IsExpanded));
                return;
            }
        }
        // Set before writing back: the item's PropertyChanged re-enters SetExpanded and must find nothing to do.
        node.IsExpandedCore = value;
        _schema.SetIsExpanded?.Invoke(node.Item, value);
        node.OnPropertyChanged(nameof(TreeNode.IsExpanded));
        if (raiseEvents)
            NodeExpandStateChanged?.Invoke(this, new NodeExpandStateChangedEventArgs(node, value));
    }

    private void Attach(TreeNode node, IEnumerable? source)
    {
        Resubscribe(node, source);
        BuildChildren(node);
    }

    private void DetachChildren(TreeNode node)
    {
        if (node.ChildrenSource is INotifyCollectionChanged observable)
            observable.CollectionChanged -= node.OnChildrenChanged;
        foreach (var child in node.ChildList)
            Detach(child);
        node.ChildList = [];
        node.ChildrenSource = null;
    }

    private void Detach(TreeNode node)
    {
        DetachChildren(node);
        if (node.Item is INotifyPropertyChanged notifier)
            notifier.PropertyChanged -= node.OnItemPropertyChanged;
        // The item may already belong to a newer node if it was added elsewhere before being removed here.
        if (_nodes.TryGetValue(node.Item, out var current) && current == node)
            _nodes.Remove(node.Item);
    }

    private TreeNode CreateNode(object item, TreeNode parent)
    {
        var node = new TreeNode(this, item, parent) { IsExpandedCore = _schema.GetIsExpanded?.Invoke(item) ?? false };
        _nodes[item] = node;
        if (_schema.IsExpandedPropertyName is not null && item is INotifyPropertyChanged notifier)
            notifier.PropertyChanged += node.OnItemPropertyChanged;
        Attach(node, _schema.GetChildren(item));
        return node;
    }

    /// <summary>Syncs a node's children with its source, reusing existing nodes so their state survives.</summary>
    private void BuildChildren(TreeNode node)
    {
        var children = new List<TreeNode>();
        if (node.ChildrenSource is not null)
            foreach (var item in node.ChildrenSource)
                children.Add(_nodes.TryGetValue(item, out var existing) && existing.ParentInternal == node
                    ? existing
                    : CreateNode(item, node));

        var kept = children.ToHashSet();
        foreach (var removed in node.ChildList.Where(c => !kept.Contains(c)))
            Detach(removed);

        var hadChildren = node.HasChildNodes;
        node.ChildList = _comparer is null ? children : children.OrderBy(c => c.Item, _comparer).ToList();
        if (hadChildren != node.HasChildNodes)
            node.OnPropertyChanged(nameof(TreeNode.HasChildNodes));
    }

    private void RebuildAll()
    {
        foreach (var node in AllNodes.Prepend(_root).ToList())
        {
            if (!node.IsRoot)
            {
                // Skip nodes an earlier rebuild in this pass removed.
                if (GetNode(node.Item) != node) continue;
                // The item may hand out a different child collection by now.
                Resubscribe(node, _schema.GetChildren(node.Item));
            }
            BuildChildren(node);
        }
    }

    private static void Resubscribe(TreeNode node, IEnumerable? source)
    {
        if (ReferenceEquals(source, node.ChildrenSource)) return;
        if (node.ChildrenSource is INotifyCollectionChanged old)
            old.CollectionChanged -= node.OnChildrenChanged;
        node.ChildrenSource = source;
        if (source is INotifyCollectionChanged observable)
            observable.CollectionChanged += node.OnChildrenChanged;
    }

    /// <summary>Recomputes <see cref="TreeNode.Matches"/> and <see cref="TreeNode.IsVisible"/> below <paramref name="node"/>; returns whether any child is visible.</summary>
    private bool ApplyFilter(TreeNode node, bool expandMatches)
    {
        var anyVisible = false;
        foreach (var child in node.ChildList)
        {
            child.Matches = !IsFiltered || (_filterLevel == FilterLevel.Root && !node.IsRoot) || PassesFilters(child.Item);
            var hasVisibleDescendant = ApplyFilter(child, expandMatches);
            child.IsVisible = child.Matches || (_filterLevel == FilterLevel.Extended && hasVisibleDescendant);
            if (expandMatches && child.IsVisible && hasVisibleDescendant)
                SetExpandedCore(child, true, raiseEvents: false);
            anyVisible |= child.IsVisible;
        }
        return anyVisible;
    }

    private void Update()
    {
        List<TreeNode> rows;
        if (_isFlat)
        {
            rows = AllNodes.Where(n => n.Matches).ToList();
            if (_comparer is not null)
                rows = rows.OrderBy(n => n.Item, _comparer).ToList();
        }
        else
        {
            rows = [];
            AddVisibleRows(_root, rows);
        }

        foreach (var node in AllNodes)
            node.ShowsExpander = !_isFlat && node.ChildList.Any(c => c.IsVisible);

        Rows.Apply(rows);
    }

    private static void AddVisibleRows(TreeNode node, List<TreeNode> rows)
    {
        foreach (var child in node.ChildList.Where(c => c.IsVisible))
        {
            rows.Add(child);
            if (child.IsExpandedCore)
                AddVisibleRows(child, rows);
        }
    }
}

public sealed class NodeExpandStateChangingEventArgs(TreeNode node, bool isExpanding) : System.ComponentModel.CancelEventArgs
{
    public TreeNode Node { get; } = node;

    public bool IsExpanding { get; } = isExpanding;
}

public sealed class NodeExpandStateChangedEventArgs(TreeNode node, bool isExpanded) : EventArgs
{
    public TreeNode Node { get; } = node;

    public bool IsExpanded { get; } = isExpanded;
}
