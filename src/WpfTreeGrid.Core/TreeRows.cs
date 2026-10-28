using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WpfTreeGrid;

/// <summary>
/// The visible rows of a <see cref="TreeModel"/>, in display order, as a read-only
/// observable list of the bound items. This is what a grid binds its ItemsSource to.
///
/// <para>Changes arrive as single-item Add/Remove notifications, which WPF collection
/// views require, and fall back to one Reset when a change touches more than
/// <see cref="MaxIncrementalChanges"/> rows.</para>
/// </summary>
public sealed class TreeRows : IList, IReadOnlyList<object>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private List<TreeNode> _rows = [];

    internal TreeRows() { }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised before and after the rows change, so a grid can keep its selection across reorders.</summary>
    public event EventHandler? Changing;

    public event EventHandler? Changed;

    public int MaxIncrementalChanges { get; set; } = 256;

    public int Count => _rows.Count;

    public object this[int index] => _rows[index].Item;

    public IReadOnlyList<TreeNode> Nodes => _rows;

    public int IndexOf(object? item)
    {
        for (var i = 0; i < _rows.Count; i++)
            if (ReferenceEquals(_rows[i].Item, item))
                return i;
        return -1;
    }

    public bool Contains(object? item) => IndexOf(item) >= 0;

    public IEnumerator<object> GetEnumerator() => _rows.Select(node => node.Item).GetEnumerator();

    /// <summary>Moves the list to <paramref name="target"/> with the fewest notifications: one block removed, one block inserted.</summary>
    internal void Apply(List<TreeNode> target)
    {
        var prefix = 0;
        while (prefix < _rows.Count && prefix < target.Count && _rows[prefix] == target[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < _rows.Count - prefix && suffix < target.Count - prefix
               && _rows[_rows.Count - 1 - suffix] == target[target.Count - 1 - suffix])
            suffix++;

        var removeCount = _rows.Count - prefix - suffix;
        var insertCount = target.Count - prefix - suffix;
        if (removeCount == 0 && insertCount == 0)
            return;

        Changing?.Invoke(this, EventArgs.Empty);

        if (removeCount + insertCount > MaxIncrementalChanges)
        {
            _rows = target;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        else
        {
            for (var i = 0; i < removeCount; i++)
            {
                var item = _rows[prefix].Item;
                _rows.RemoveAt(prefix);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, prefix));
            }

            for (var i = 0; i < insertCount; i++)
            {
                var node = target[prefix + i];
                _rows.Insert(prefix + i, node);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, node.Item, prefix + i));
            }
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool IList.IsReadOnly => true;

    bool IList.IsFixedSize => true;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    void ICollection.CopyTo(Array array, int index)
    {
        foreach (var node in _rows)
            array.SetValue(node.Item, index++);
    }

    int IList.Add(object? value) => throw new NotSupportedException();

    void IList.Clear() => throw new NotSupportedException();

    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    void IList.Remove(object? value) => throw new NotSupportedException();

    void IList.RemoveAt(int index) => throw new NotSupportedException();
}
