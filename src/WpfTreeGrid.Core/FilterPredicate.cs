using System.Globalization;

namespace WpfTreeGrid;

// A column filter is a list of predicates: a type (Equals, Contains, LessThan...), a value, how it
// joins the predicates before it, and whether it compares as text or by the cell's own type.

public enum FilterType
{
    Equals,
    NotEquals,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Contains,
    NotContains,
    StartsWith,
    NotStartsWith,
    EndsWith,
    NotEndsWith,
}

/// <summary>How a predicate combines with the result of the predicates before it.</summary>
public enum PredicateType
{
    And,
    Or,
    AndAlso,
    OrElse,
}

/// <summary>Whether <see cref="FilterPredicate.FilterValue"/> is compared as text or as the cell's own type.</summary>
public enum FilterBehavior
{
    StringTyped,
    StronglyTyped,
}

/// <summary>
/// One condition of a column filter. A null <see cref="FilterValue"/> with
/// <see cref="FilterType.Equals"/> matches blank cells (null or empty text).
/// </summary>
public sealed class FilterPredicate
{
    public FilterType FilterType { get; set; }

    public object? FilterValue { get; set; }

    public PredicateType PredicateType { get; set; }

    public bool IsCaseSensitive { get; set; }

    public FilterBehavior FilterBehavior { get; set; }

    /// <summary>
    /// Evaluates a column's predicates against one cell value. Each predicate's
    /// <see cref="PredicateType"/> joins it to the predicates before it (the first one's is ignored),
    /// with this precedence: <c>And</c>/<c>Or</c>
    /// bind tighter than <c>AndAlso</c>/<c>OrElse</c>, and equal levels go left to right. So
    /// <c>A, AndAlso B, Or C</c> is <c>A and (B or C)</c>, but <c>A, Or B, And C</c> is <c>(A or B) and C</c>.
    /// An empty list matches everything.
    /// </summary>
    public static bool Matches(IReadOnlyList<FilterPredicate> predicates, object? cellValue)
    {
        if (predicates.Count == 0) return true;

        bool? result = null;
        var outerJoin = PredicateType.AndAlso;
        var i = 0;
        while (i < predicates.Count)
        {
            // A run joined by And/Or is evaluated first, left to right.
            var group = predicates[i].Matches(cellValue);
            var j = i + 1;
            for (; j < predicates.Count && predicates[j].PredicateType is PredicateType.And or PredicateType.Or; j++)
                group = predicates[j].PredicateType == PredicateType.And
                    ? group && predicates[j].Matches(cellValue)
                    : group || predicates[j].Matches(cellValue);

            result = result is null ? group : outerJoin == PredicateType.AndAlso ? result.Value && group : result.Value || group;
            if (j < predicates.Count)
                outerJoin = predicates[j].PredicateType;
            i = j;
        }
        return result!.Value;
    }

    /// <summary>
    /// Human-readable summary of a column's predicates: "Type: Part, Assembly, +2" for a value
    /// checklist (Equals joined by Or), otherwise e.g. "Mass &gt; 5 and &lt; 9".
    /// </summary>
    public static string Describe(string columnName, IReadOnlyList<FilterPredicate> predicates)
    {
        if (predicates.Count == 0) return "";
        if (predicates.All(p => p.FilterType == FilterType.Equals) && predicates.Skip(1).All(p => p.PredicateType is PredicateType.Or or PredicateType.OrElse))
        {
            var shown = predicates.Take(3).Select(p => FilterText.Of(p.FilterValue)).ToList();
            var more = predicates.Count - shown.Count;
            return $"{columnName}: {string.Join(", ", shown)}{(more > 0 ? $", +{more}" : "")}";
        }
        var parts = predicates.Select((p, i) => i == 0
            ? p.ToString()
            : $"{(p.PredicateType is PredicateType.And or PredicateType.AndAlso ? "and" : "or")} {p}");
        return $"{columnName} {string.Join(" ", parts)}";
    }

    /// <summary>
    /// Matches one cell:
    /// <list type="bullet">
    /// <item>A null <see cref="FilterValue"/> asks "is the cell null": Equals/Contains/StartsWith/EndsWith
    /// match null cells only, their Not variants every other cell. Null and "" are different values.</item>
    /// <item>StringTyped Equals compares the cell's display text in the current culture, so "12.3"
    /// does not equal 12,3 on a Dutch machine; StronglyTyped compares by the cell's type.</item>
    /// <item>Relational types compare numbers, dates and booleans by value, also in a StringTyped
    /// predicate; text types work in a StronglyTyped one too.</item>
    /// </list>
    /// </summary>
    public bool Matches(object? cellValue)
    {
        if (FilterValue is null)
            return FilterType switch
            {
                FilterType.Equals or FilterType.Contains or FilterType.StartsWith or FilterType.EndsWith => cellValue is null,
                FilterType.NotEquals or FilterType.NotContains or FilterType.NotStartsWith or FilterType.NotEndsWith => cellValue is not null,
                _ => false,
            };

        var comparison = IsCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
        var text = FilterText.Of(cellValue, blank: "");
        var value = FilterText.Of(FilterValue, blank: "");

        return FilterType switch
        {
            FilterType.Equals => IsEqual(cellValue, text, value, comparison),
            FilterType.NotEquals => !IsEqual(cellValue, text, value, comparison),
            FilterType.Contains => text.Contains(value, comparison),
            FilterType.NotContains => !text.Contains(value, comparison),
            FilterType.StartsWith => text.StartsWith(value, comparison),
            FilterType.NotStartsWith => !text.StartsWith(value, comparison),
            FilterType.EndsWith => text.EndsWith(value, comparison),
            FilterType.NotEndsWith => !text.EndsWith(value, comparison),
            _ when cellValue is null => false,
            FilterType.LessThan => Compare(cellValue, text, comparison) < 0,
            FilterType.LessThanOrEqual => Compare(cellValue, text, comparison) <= 0,
            FilterType.GreaterThan => Compare(cellValue, text, comparison) > 0,
            FilterType.GreaterThanOrEqual => Compare(cellValue, text, comparison) >= 0,
            _ => throw new ArgumentOutOfRangeException(nameof(FilterType), FilterType, null),
        };
    }

    private bool IsEqual(object? cellValue, string text, string value, StringComparison comparison) =>
        cellValue is not null && (FilterBehavior == FilterBehavior.StringTyped
            ? string.Equals(text, value, comparison)
            : Compare(cellValue, text, comparison) == 0);

    public override string ToString() => (FilterType, FilterValue) switch
    {
        (FilterType.Equals, null) => "is empty",
        (FilterType.NotEquals, null) => "is not empty",
        _ => $"{Symbol(FilterType)} {(FilterValue is string ? $"'{FilterValue}'" : FilterText.Of(FilterValue))}",
    };

    /// <summary>
    /// Compares the cell with <see cref="FilterValue"/>: numerically, by date or as booleans when
    /// the cell has that type and the filter value converts to it — for StringTyped predicates
    /// too, so "Mass &gt; 5" never degrades to a text comparison — and as text otherwise.
    /// </summary>
    private int Compare(object? cellValue, string text, StringComparison comparison)
    {
        if (cellValue is not null && ValueComparer.IsNumber(cellValue) && TryGetDouble(FilterValue, out var number))
            return Convert.ToDouble(cellValue).CompareTo(number);
        if (cellValue is DateTime date && TryGetDate(FilterValue, out var otherDate))
            return date.CompareTo(otherDate);
        if (cellValue is bool flag && TryGetBool(FilterValue, out var otherFlag))
            return flag.CompareTo(otherFlag);
        return string.Compare(text, FilterText.Of(FilterValue, blank: ""), comparison);
    }

    private static bool TryGetDouble(object? value, out double number)
    {
        if (value is not null && ValueComparer.IsNumber(value))
        {
            number = Convert.ToDouble(value);
            return true;
        }
        var text = value as string;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number)
               || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryGetDate(object? value, out DateTime date)
    {
        if (value is DateTime typed)
        {
            date = typed;
            return true;
        }
        return DateTime.TryParse(value as string, CultureInfo.CurrentCulture, out date);
    }

    private static bool TryGetBool(object? value, out bool flag)
    {
        if (value is bool typed)
        {
            flag = typed;
            return true;
        }
        return bool.TryParse(value as string, out flag);
    }

    private static string Symbol(FilterType type) => type switch
    {
        FilterType.Equals => "=",
        FilterType.NotEquals => "≠",
        FilterType.LessThan => "<",
        FilterType.LessThanOrEqual => "≤",
        FilterType.GreaterThan => ">",
        FilterType.GreaterThanOrEqual => "≥",
        FilterType.Contains => "contains",
        FilterType.NotContains => "does not contain",
        FilterType.StartsWith => "starts with",
        FilterType.NotStartsWith => "does not start with",
        FilterType.EndsWith => "ends with",
        FilterType.NotEndsWith => "does not end with",
        _ => type.ToString(),
    };
}

public static class FilterText
{
    public const string Blank = "(Blanks)";

    /// <summary>The text a value is listed and matched under in a filter checklist.</summary>
    public static string Of(object? value, string blank = Blank)
    {
        var text = value switch
        {
            null => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? "",
        };
        return text.Length == 0 ? blank : text;
    }
}
