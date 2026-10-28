using System.Collections.Specialized;

namespace WpfTreeGrid.Tests;

/// <summary>
/// Rebuilds the row list purely from <see cref="TreeRows"/> change notifications, the
/// way a WPF collection view does. If it ever disagrees with the real list, an event
/// carried a wrong index or item.
/// </summary>
internal sealed class RowsMirror
{
    private readonly TreeRows _rows;
    private readonly List<object> _items;

    public RowsMirror(TreeRows rows)
    {
        _rows = rows;
        _items = rows.ToList();
        rows.CollectionChanged += OnCollectionChanged;
    }

    public List<NotifyCollectionChangedEventArgs> Events { get; } = [];

    public void AssertInSync() => Assert.Equal(_rows.ToList(), _items);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Events.Add(e);
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Assert.Same(e.NewItems![0], _rows[e.NewStartingIndex]);
                _items.Insert(e.NewStartingIndex, e.NewItems[0]!);
                break;
            case NotifyCollectionChangedAction.Remove:
                Assert.Same(e.OldItems![0], _items[e.OldStartingIndex]);
                _items.RemoveAt(e.OldStartingIndex);
                break;
            case NotifyCollectionChangedAction.Reset:
                _items.Clear();
                _items.AddRange(_rows);
                break;
            default:
                throw new InvalidOperationException($"Unexpected {e.Action}");
        }
    }
}
