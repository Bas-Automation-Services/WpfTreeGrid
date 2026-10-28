using System.Reflection;

namespace WpfTreeGrid;

/// <summary>
/// Reads a dotted property path (<c>Document.Mass</c>) or a string-keyed indexer
/// (<c>Properties[Mass]</c>) off an item, the subset of WPF binding paths that
/// grid columns use. A missing property or null along the way yields null.
/// </summary>
public sealed class MemberPath
{
    private readonly Segment[] _segments;
    private readonly Dictionary<(Type Type, int Index), PropertyInfo?> _properties = new();

    public MemberPath(string path)
    {
        Path = path;
        _segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries).SelectMany(Parse).ToArray();
    }

    public string Path { get; }

    public object? GetValue(object? item)
    {
        for (var i = 0; i < _segments.Length && item is not null; i++)
        {
            var key = (item.GetType(), i);
            if (!_properties.TryGetValue(key, out var property))
                _properties[key] = property = _segments[i].Find(key.Item1);
            item = property is null ? null : _segments[i].Read(property, item);
        }
        return item;
    }

    public override string ToString() => Path;

    private static IEnumerable<Segment> Parse(string part)
    {
        var bracket = part.IndexOf('[');
        if (bracket < 0)
        {
            yield return new Segment(part, null);
            yield break;
        }
        if (bracket > 0)
            yield return new Segment(part[..bracket], null);
        yield return new Segment("Item", part[(bracket + 1)..part.IndexOf(']')]);
    }

    private sealed record Segment(string Name, string? Key)
    {
        public PropertyInfo? Find(Type type) => Key is null
            ? type.GetProperty(Name)
            : type.GetProperties().FirstOrDefault(p => p.GetIndexParameters() is [{ ParameterType: var t }] && t == typeof(string));

        public object? Read(PropertyInfo property, object item) => Key is null
            ? property.GetValue(item)
            : ReadIndexer(property, item, Key);

        private static object? ReadIndexer(PropertyInfo property, object item, string key)
        {
            // Dictionary indexers throw on a missing key; a missing value just reads as empty.
            try { return property.GetValue(item, [key]); }
            catch (TargetInvocationException e) when (e.InnerException is KeyNotFoundException) { return null; }
        }
    }
}
