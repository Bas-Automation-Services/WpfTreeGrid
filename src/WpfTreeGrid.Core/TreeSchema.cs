using System.Collections;
using System.Reflection;

namespace WpfTreeGrid;

/// <summary>
/// How a <see cref="TreeModel"/> reads the hierarchy and expand state off the bound
/// items. Build one from delegates, or from property names with
/// <see cref="FromPropertyNames"/>.
/// </summary>
public sealed class TreeSchema
{
    public TreeSchema(Func<object, IEnumerable?> getChildren) => GetChildren = getChildren;

    /// <summary>Child collection of an item; null or empty for a leaf. Observed when it implements <c>INotifyCollectionChanged</c>.</summary>
    public Func<object, IEnumerable?> GetChildren { get; }

    /// <summary>Initial expand state of an item. Null leaves every node collapsed.</summary>
    public Func<object, bool>? GetIsExpanded { get; init; }

    /// <summary>Writes the node's expand state back to the item. Null keeps it on the node only.</summary>
    public Action<object, bool>? SetIsExpanded { get; init; }

    /// <summary>
    /// Property name the item raises <c>PropertyChanged</c> for when its expand state
    /// changes, so a view-model can expand or collapse rows. Null means not observed.
    /// </summary>
    public string? IsExpandedPropertyName { get; init; }

    /// <summary>The same expand-state handling with a different way to find children.</summary>
    public TreeSchema WithChildren(Func<object, IEnumerable?> getChildren) => new(getChildren)
    {
        GetIsExpanded = GetIsExpanded,
        SetIsExpanded = SetIsExpanded,
        IsExpandedPropertyName = IsExpandedPropertyName,
    };

    /// <summary>
    /// Reflection-based schema. Items without the named properties are treated as
    /// leaves and as collapsed, so a flat list needs no dummy <c>Children</c>.
    /// </summary>
    public static TreeSchema FromPropertyNames(string childrenPropertyName, string? isExpandedPropertyName = null)
    {
        var children = new PropertyCache(childrenPropertyName);
        if (string.IsNullOrEmpty(isExpandedPropertyName))
            return new TreeSchema(item => children.Get(item) as IEnumerable);

        var expanded = new PropertyCache(isExpandedPropertyName);
        return new TreeSchema(item => children.Get(item) as IEnumerable)
        {
            GetIsExpanded = item => expanded.Get(item) is true,
            SetIsExpanded = (item, value) => expanded.Set(item, value),
            IsExpandedPropertyName = isExpandedPropertyName,
        };
    }

    private sealed class PropertyCache(string name)
    {
        private readonly Dictionary<Type, PropertyInfo?> _properties = new();

        public object? Get(object item) => Find(item)?.GetValue(item);

        public void Set(object item, object? value)
        {
            if (Find(item) is { CanWrite: true } property)
                property.SetValue(item, value);
        }

        private PropertyInfo? Find(object item)
        {
            var type = item.GetType();
            if (!_properties.TryGetValue(type, out var property))
                _properties[type] = property = type.GetProperty(name);
            return property;
        }
    }
}
