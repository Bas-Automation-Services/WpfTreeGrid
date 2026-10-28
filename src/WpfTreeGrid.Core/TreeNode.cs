using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WpfTreeGrid;

/// <summary>
/// Tree position of one bound item: its parent, depth, children and expand state.
/// Owned by a <see cref="TreeModel"/>; look one up with <see cref="TreeModel.GetNode"/>.
/// Bind the expander cell to it.
/// </summary>
public sealed class TreeNode : INotifyPropertyChanged
{
    private readonly TreeModel _model;
    private readonly TreeNode? _parent;
    private bool _showsExpander;

    internal TreeNode(TreeModel model, object item, TreeNode? parent)
    {
        _model = model;
        _parent = parent;
        Item = item;
        Level = parent is null ? -1 : parent.Level + 1;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public object Item { get; }

    /// <summary>Parent node, or null for a top-level row.</summary>
    public TreeNode? ParentNode => _parent?._parent is null ? null : _parent;

    /// <summary>Depth in the tree; top-level rows are 0.</summary>
    public int Level { get; }

    /// <summary>Indent to render: <see cref="Level"/> in tree mode, 0 in flat mode.</summary>
    public int DisplayLevel => _model.IsFlat ? 0 : Level;

    public IReadOnlyList<TreeNode> ChildNodes => ChildList;

    public bool HasChildNodes => ChildList.Count > 0;

    /// <summary>Whether any child survives the filter, regardless of expand state.</summary>
    public bool HasVisibleChildNodes => ChildList.Any(c => c.IsVisible);

    public bool IsExpanded
    {
        get => IsExpandedCore;
        set => _model.SetExpanded(this, value);
    }

    /// <summary>
    /// Whether the row gets an expand toggle: tree mode, and at least one child that
    /// survives the filter.
    /// </summary>
    public bool ShowsExpander
    {
        get => _showsExpander;
        internal set
        {
            if (_showsExpander == value) return;
            _showsExpander = value;
            OnPropertyChanged(nameof(ShowsExpander));
        }
    }

    public IEnumerable<TreeNode> Descendants()
    {
        foreach (var child in ChildList)
        {
            yield return child;
            foreach (var descendant in child.Descendants())
                yield return descendant;
        }
    }

    public override string ToString() => $"{Item} (level {Level})";

    internal bool IsRoot => _parent is null;

    internal TreeNode? ParentInternal => _parent;

    internal bool IsExpandedCore { get; set; }

    internal List<TreeNode> ChildList { get; set; } = [];

    internal IEnumerable? ChildrenSource { get; set; }

    /// <summary>Passes the filter itself (always true without a filter).</summary>
    internal bool Matches { get; set; } = true;

    /// <summary>Shown in tree mode: matches, or has a matching descendant.</summary>
    internal bool IsVisible { get; set; } = true;

    internal void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) => _model.OnChildrenChanged(this);

    internal void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => _model.OnItemPropertyChanged(this, e);

    internal void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
