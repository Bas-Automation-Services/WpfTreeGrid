using System.Collections;
using System.Globalization;

namespace WpfTreeGrid;

/// <summary>
/// Builds a tree from a flat list in which each item names its parent by key, the
/// self-relational mode (<c>ChildPropertyName</c> = key, <c>ParentPropertyName</c> = parent key).
/// Items whose parent key equals <see cref="RootValue"/> (or is null when that is null) are the roots.
/// </summary>
public sealed class SelfRelation(string keyPath, string parentKeyPath, object? rootValue)
{
    private readonly MemberPath _key = new(keyPath);
    private readonly MemberPath _parentKey = new(parentKeyPath);
    private readonly List<object> _roots = [];
    private readonly Dictionary<object, List<object>> _children = new();

    public object? RootValue { get; } = rootValue;

    /// <summary>The top-level items; a stable list instance that <see cref="Rebuild"/> refills.</summary>
    public IReadOnlyList<object> Roots => _roots;

    public IEnumerable? ChildrenOf(object item) =>
        _key.GetValue(item) is { } key && _children.TryGetValue(key, out var children) ? children : null;

    public void Rebuild(IEnumerable? source)
    {
        _roots.Clear();
        _children.Clear();
        foreach (var item in source?.Cast<object>() ?? [])
        {
            var parent = _parentKey.GetValue(item);
            if (IsRoot(parent))
                _roots.Add(item);
            else if (_children.TryGetValue(parent!, out var siblings))
                siblings.Add(item);
            else
                _children[parent!] = [item];
        }
    }

    // A root value set in XAML arrives as a string ("-1"), so also compare by invariant text.
    private bool IsRoot(object? parent) =>
        parent is null
        || Equals(parent, RootValue)
        || (RootValue is string text && Convert.ToString(parent, CultureInfo.InvariantCulture) == text);
}
