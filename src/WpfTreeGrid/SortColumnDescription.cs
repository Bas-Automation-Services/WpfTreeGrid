using System.ComponentModel;

namespace WpfTreeGrid;

/// <summary>One sort key: the column's MappingName and the direction.</summary>
public sealed class SortColumnDescription
{
    public string? ColumnName { get; set; }

    public ListSortDirection SortDirection { get; set; }
}
