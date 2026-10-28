using System.Globalization;

namespace WpfTreeGrid.Tests;

public class MemberPathTests
{
    private sealed record Doc(string Name, double Mass);

    private sealed record Row(Doc? Document, Dictionary<string, object> Properties);

    private static readonly Row Sample = new(new Doc("Bracket", 1.5), new() { ["Material"] = "Steel" });

    [Theory]
    [InlineData("Document.Name", "Bracket")]
    [InlineData("Document.Mass", 1.5)]
    [InlineData("Properties[Material]", "Steel")]
    [InlineData("Properties[Missing]", null)]
    [InlineData("Document.Nope", null)]
    public void ReadsPath(string path, object? expected) => Assert.Equal(expected, new MemberPath(path).GetValue(Sample));

    [Fact]
    public void NullAlongThePathReadsAsNull() => Assert.Null(new MemberPath("Document.Name").GetValue(Sample with { Document = null }));
}

public class ValueComparerTests
{
    [Theory]
    [InlineData("Part2", "Part10", -1)]
    [InlineData("part10", "Part2", 1)]
    [InlineData("Bolt", "bolt", -1)] // case-insensitive tie broken ordinally, so the order is stable
    [InlineData("A01", "A1", -1)]
    [InlineData("Plate", "Plate-1", -1)]
    public void ComparesStringsNaturally(string x, string y, int sign) => Assert.Equal(sign, Math.Sign(ValueComparer.Compare(x, y)));

    [Fact]
    public void ComparesNumbersAcrossTypesAndPutsNullFirst()
    {
        Assert.True(ValueComparer.Compare(2, 10.5) < 0);
        Assert.True(ValueComparer.Compare(3m, 2L) > 0);
        Assert.True(ValueComparer.Compare(null, 0) < 0);
    }

    [Fact]
    public void CreateAppliesKeysInOrderWithDirection()
    {
        var items = new[] { ("b", 1), ("a", 1), ("c", 0) }.Select(t => new { Name = t.Item1, Group = t.Item2 }).ToList<object>();

        items.Sort(ValueComparer.Create([new SortKey(new MemberPath("Group"), Descending: true), new SortKey(new MemberPath("Name"))]));

        Assert.Equal("a,b,c", string.Join(",", items.Select(i => ((dynamic)i).Name)));
    }
}

public class FilterPredicateTests
{
    public FilterPredicateTests() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    private static FilterPredicate P(FilterType type, object? value, PredicateType join = PredicateType.And) =>
        new() { FilterType = type, FilterValue = value, PredicateType = join };

    [Theory]
    [InlineData(FilterType.GreaterThan, 5.0, 12.3, true)] // numeric, not "12.3" < "5" as text
    [InlineData(FilterType.GreaterThan, "5", 12.3, true)] // StringTyped value still compares numerically against a number
    [InlineData(FilterType.GreaterThan, 5.0, 4, false)]
    [InlineData(FilterType.LessThanOrEqual, 5, 5.0, true)]
    [InlineData(FilterType.GreaterThan, 5.0, null, false)]
    [InlineData(FilterType.Equals, "steel", "Steel", true)]
    [InlineData(FilterType.Equals, null, "", false)] // null and "" are different values
    [InlineData(FilterType.Equals, "", null, false)]
    [InlineData(FilterType.Equals, "", "", true)]
    [InlineData(FilterType.Equals, null, null, true)]
    [InlineData(FilterType.Contains, null, null, true)] // a null value asks "is the cell null"
    [InlineData(FilterType.Contains, null, "x", false)]
    [InlineData(FilterType.NotContains, null, "x", true)]
    [InlineData(FilterType.NotEquals, null, "x", true)]
    [InlineData(FilterType.NotEquals, "Steel", null, true)]
    [InlineData(FilterType.Contains, "olt", "Bolt M8", true)]
    [InlineData(FilterType.NotContains, "olt", "Nut", true)]
    [InlineData(FilterType.StartsWith, "bo", "Bolt", true)]
    [InlineData(FilterType.NotStartsWith, "bo", "Bolt", false)]
    [InlineData(FilterType.EndsWith, "m8", "Bolt M8", true)]
    [InlineData(FilterType.NotEndsWith, "m8", "Nut", true)]
    [InlineData(FilterType.Equals, true, true, true)]
    public void PredicateMatches(FilterType type, object? value, object? cell, bool expected) =>
        Assert.Equal(expected, P(type, value).Matches(cell));

    [Fact]
    public void CaseSensitivityIsOptIn()
    {
        Assert.True(P(FilterType.Equals, "bolt").Matches("Bolt"));
        Assert.False(new FilterPredicate { FilterType = FilterType.Equals, FilterValue = "bolt", IsCaseSensitive = true }.Matches("Bolt"));
    }

    [Fact]
    public void ListIsFoldedLeftToRight()
    {
        // Checklist shape: Equals joined by Or.
        FilterPredicate[] checklist = [P(FilterType.Equals, "Part"), P(FilterType.Equals, null, PredicateType.Or)];
        Assert.True(FilterPredicate.Matches(checklist, "Part"));
        Assert.True(FilterPredicate.Matches(checklist, null));
        Assert.False(FilterPredicate.Matches(checklist, "Assembly"));

        // Range: > 1 And < 3.
        FilterPredicate[] range = [P(FilterType.GreaterThan, 1), P(FilterType.LessThan, 3)];
        Assert.True(FilterPredicate.Matches(range, 2));
        Assert.False(FilterPredicate.Matches(range, 5));
        Assert.True(FilterPredicate.Matches([P(FilterType.GreaterThan, 1), P(FilterType.LessThan, 3, PredicateType.OrElse)], 5));
        Assert.True(FilterPredicate.Matches([], "anything"));

        // Precedence: And/Or bind tighter than AndAlso/OrElse.
        // != 5, AndAlso >= 0.5, Or >= 5  is  != 5 and (>= 0.5 or >= 5).
        FilterPredicate[] mixed = [P(FilterType.NotEquals, 5.0), P(FilterType.GreaterThanOrEqual, 0.5, PredicateType.AndAlso), P(FilterType.GreaterThanOrEqual, 5.0, PredicateType.Or)];
        Assert.False(FilterPredicate.Matches(mixed, 5.0));
        Assert.True(FilterPredicate.Matches(mixed, 1.0));
        Assert.False(FilterPredicate.Matches(mixed, 0.0));

        // > 5, Or != 0.5, And = 0.5  is  (> 5 or != 0.5) and = 0.5: same level, left to right.
        FilterPredicate[] sameLevel = [P(FilterType.GreaterThan, 5.0), P(FilterType.NotEquals, 0.5, PredicateType.Or), P(FilterType.Equals, 0.5, PredicateType.And)];
        Assert.False(FilterPredicate.Matches(sameLevel, 0.5));
        Assert.False(FilterPredicate.Matches(sameLevel, 10.0));
    }

    [Fact]
    public void DescribeSummarisesChecklistsAndConditions()
    {
        Assert.Equal("Mass > 5", FilterPredicate.Describe("Mass", [P(FilterType.GreaterThan, 5)]));
        Assert.Equal("Name contains 'bolt' or = 'Nut'",
            FilterPredicate.Describe("Name", [P(FilterType.Contains, "bolt"), P(FilterType.Equals, "Nut", PredicateType.Or)]));
        Assert.Equal("Type: Part, Assembly, Sheet, +1",
            FilterPredicate.Describe("Type", [P(FilterType.Equals, "Part"), P(FilterType.Equals, "Assembly", PredicateType.Or),
                P(FilterType.Equals, "Sheet", PredicateType.Or), P(FilterType.Equals, "Weldment", PredicateType.Or)]));
        Assert.Equal("Material is empty and = 'x'", FilterPredicate.Describe("Material", [P(FilterType.Equals, null), P(FilterType.Equals, "x")]));
    }
}

public class SelfRelationTests
{
    private sealed record Row(int Id, int? ParentId, string Name);

    [Fact]
    public void BuildsChildrenFromParentKeysWithStringRootValue()
    {
        var rows = new[] { new Row(1, -1, "Top"), new Row(2, 1, "Sub"), new Row(3, 2, "Leaf"), new Row(4, -1, "Other") };
        var relation = new SelfRelation("Id", "ParentId", "-1");

        relation.Rebuild(rows);

        Assert.Equal(["Top", "Other"], relation.Roots.Cast<Row>().Select(r => r.Name));
        Assert.Equal(["Sub"], relation.ChildrenOf(rows[0])!.Cast<Row>().Select(r => r.Name));
        Assert.Null(relation.ChildrenOf(rows[2]));
    }

    [Fact]
    public void NullParentIsARootWhenNoRootValueIsSet()
    {
        var rows = new[] { new Row(1, null, "Top"), new Row(2, 1, "Sub") };
        var relation = new SelfRelation("Id", "ParentId", null);

        relation.Rebuild(rows);

        var model = new TreeModel(TreeSchema.FromPropertyNames("Id").WithChildren(relation.ChildrenOf)) { AutoExpandMode = AutoExpandMode.AllNodesExpanded, ItemsSource = relation.Roots };
        Assert.Equal(["Top", "Sub"], model.Rows.Cast<Row>().Select(r => r.Name));
    }
}
