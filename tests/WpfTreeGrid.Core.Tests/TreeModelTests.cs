using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace WpfTreeGrid.Tests;

public class TreeModelTests
{
    // A
    //   A1
    //     A1a
    //   A2
    // B
    //   B1
    // C
    private readonly TestItem _a1a = new("A1a");
    private readonly TestItem _a1;
    private readonly TestItem _a2 = new("A2");
    private readonly TestItem _a;
    private readonly TestItem _b1 = new("B1");
    private readonly TestItem _b;
    private readonly TestItem _c = new("C");
    private readonly ObservableCollection<TestItem> _roots;
    private readonly TreeModel _model;
    private readonly RowsMirror _mirror;

    public TreeModelTests()
    {
        _a1 = new TestItem("A1", _a1a);
        _a = new TestItem("A", _a1, _a2);
        _b = new TestItem("B", _b1);
        _roots = [_a, _b, _c];
        _model = new TreeModel(TreeSchema.FromPropertyNames(nameof(TestItem.Children), nameof(TestItem.IsExpanded)))
        {
            ItemsSource = _roots,
        };
        _mirror = new RowsMirror(_model.Rows);
    }

    private string Visible()
    {
        _mirror.AssertInSync();
        return string.Join(",", _model.Rows);
    }

    private TreeNode Node(TestItem item) => _model.GetNode(item)!;

    private static int ByName(object x, object y) => string.CompareOrdinal(((TestItem)x).Name, ((TestItem)y).Name);

    [Fact]
    public void TopLevelRowsStartCollapsed()
    {
        Assert.Equal("A,B,C", Visible());
        Assert.All(_model.Nodes, n => Assert.Equal(0, n.Level));
        Assert.Null(Node(_a).ParentNode);
    }

    [Fact]
    public void ExpandingInsertsChildrenBelowParent()
    {
        Node(_a).IsExpanded = true;

        Assert.Equal("A,A1,A2,B,C", Visible());
        Assert.Equal(1, Node(_a1).Level);
        Assert.Same(Node(_a), Node(_a1).ParentNode);
    }

    [Fact]
    public void ExpandingRaisesOneAddPerRowAtItsIndex()
    {
        Node(_a).IsExpanded = true;

        Assert.Collection(_mirror.Events,
            e => Assert.Equal((NotifyCollectionChangedAction.Add, 1), (e.Action, e.NewStartingIndex)),
            e => Assert.Equal((NotifyCollectionChangedAction.Add, 2), (e.Action, e.NewStartingIndex)));
    }

    [Fact]
    public void CollapsingRemovesWholeSubtreeAndKeepsNestedState()
    {
        Node(_a).IsExpanded = true;
        Node(_a1).IsExpanded = true;
        Assert.Equal("A,A1,A1a,A2,B,C", Visible());

        Node(_a).IsExpanded = false;
        Assert.Equal("A,B,C", Visible());

        Node(_a).IsExpanded = true;
        Assert.Equal("A,A1,A1a,A2,B,C", Visible());
    }

    [Fact]
    public void ExpandStateIsWrittenBackToItem()
    {
        Node(_a).IsExpanded = true;

        Assert.True(_a.IsExpanded);
    }

    [Fact]
    public void ItemExpandStateChangeExpandsRow()
    {
        _a.IsExpanded = true;

        Assert.Equal("A,A1,A2,B,C", Visible());
        Assert.True(Node(_a).IsExpanded);
    }

    [Fact]
    public void InitialExpandStateIsReadFromItem()
    {
        var root = new TestItem("R", new TestItem("R1")) { IsExpanded = true };
        var model = new TreeModel(TreeSchema.FromPropertyNames("Children", "IsExpanded")) { ItemsSource = new[] { root } };

        Assert.Equal("R,R1", string.Join(",", model.Rows));
    }

    [Theory]
    [InlineData(AutoExpandMode.RootNodesExpanded, "A,A1,A2,B,B1,C")]
    [InlineData(AutoExpandMode.AllNodesExpanded, "A,A1,A1a,A2,B,B1,C")]
    public void AutoExpandModeAppliesWhenItemsSourceIsSet(AutoExpandMode mode, string expected)
    {
        var model = new TreeModel(TreeSchema.FromPropertyNames("Children", "IsExpanded")) { AutoExpandMode = mode, ItemsSource = _roots };

        Assert.Equal(expected, string.Join(",", model.Rows));
    }

    [Fact]
    public void ExpandToLevelOpensOnlyThatManyLevels()
    {
        _model.ExpandAll();

        _model.ExpandToLevel(1);

        Assert.Equal("A,A1,A2,B,B1,C", Visible());
        Assert.False(_a1.IsExpanded);
    }

    [Fact]
    public void ExpandAllAndCollapseAllScopeToNode()
    {
        _model.ExpandAll(Node(_a));
        Assert.Equal("A,A1,A1a,A2,B,C", Visible());

        _model.ExpandAll();
        _model.CollapseAll(Node(_a));
        Assert.Equal("A,B,B1,C", Visible());
        Assert.False(_a1.IsExpanded);
    }

    [Fact]
    public void AddingChildToExpandedParentInsertsRow()
    {
        Node(_a).IsExpanded = true;

        _a.Children.Add(new TestItem("A3"));

        Assert.Equal("A,A1,A2,A3,B,C", Visible());
    }

    [Fact]
    public void AddingFirstChildShowsExpanderWithoutAddingRow()
    {
        Assert.False(Node(_c).ShowsExpander);

        _c.Children.Add(new TestItem("C1"));

        Assert.True(Node(_c).ShowsExpander);
        Assert.True(Node(_c).HasChildNodes);
        Assert.Equal("A,B,C", Visible());
    }

    [Fact]
    public void RemovingChildRemovesItsSubtreeAndStopsObservingIt()
    {
        _model.ExpandAll();

        _a.Children.Remove(_a1);
        Assert.Equal("A,A2,B,B1,C", Visible());
        Assert.Null(_model.GetNode(_a1));
        Assert.Null(_model.GetNode(_a1a));

        _a1.Children.Add(new TestItem("orphan"));
        _a1.IsExpanded = false;
        Assert.Equal("A,A2,B,B1,C", Visible());
    }

    [Fact]
    public void ClearingChildrenRemovesRowsAndExpander()
    {
        _model.ExpandAll();

        _a.Children.Clear();

        Assert.Equal("A,B,B1,C", Visible());
        Assert.False(Node(_a).ShowsExpander);
    }

    [Fact]
    public void TopLevelCollectionChangesAreTracked()
    {
        _roots.Add(new TestItem("D"));
        _roots.Remove(_b);
        _roots.Move(0, 1);

        Assert.Equal("C,A,D", Visible());
    }

    [Fact]
    public void ItemMovedToAnotherParentFollowsIt()
    {
        _model.ExpandAll();

        // Add-before-remove: the item briefly has two parents.
        _b.Children.Add(_a2);
        _a.Children.Remove(_a2);

        Assert.Equal("A,A1,A1a,B,B1,A2,C", Visible());
        Assert.Same(Node(_b), Node(_a2).ParentNode);
    }

    [Fact]
    public void ReplacingItemsSourceDetachesTheOldOne()
    {
        _model.ItemsSource = new[] { new TestItem("X") };

        _roots.Add(new TestItem("D"));
        _a.IsExpanded = true;

        Assert.Equal("X", Visible());
        Assert.Null(_model.GetNode(_a));
    }

    [Fact]
    public void SortOrdersSiblingsAndKeepsChildrenUnderTheirParent()
    {
        _model.ExpandAll();

        _model.Sort = (x, y) => ByName(y, x);

        Assert.Equal("C,B,B1,A,A2,A1,A1a", Visible());
    }

    [Fact]
    public void SortIsStableAndNullRestoresSourceOrder()
    {
        _model.Sort = (_, _) => 0;
        Assert.Equal("A,B,C", Visible());

        _model.Sort = (x, y) => ByName(y, x);
        _model.Sort = null;
        Assert.Equal("A,B,C", Visible());
    }

    [Fact]
    public void ChildAddedUnderSortedParentLandsInSortedPosition()
    {
        _model.Sort = ByName;
        Node(_a).IsExpanded = true;

        _a.Children.Add(new TestItem("A0"));

        Assert.Equal("A,A0,A1,A2,B,C", Visible());
    }

    [Fact]
    public void ExtendedFilterKeepsAncestorsOfMatchesAndCanExpandThem()
    {
        _model.FilterLevel = FilterLevel.Extended;
        _model.ExpandFilterMatches = true;
        _model.Filter = item => ((TestItem)item).Name == "A1a";

        Assert.Equal("A,A1,A1a", Visible());
        Assert.True(_a.IsExpanded);
        Assert.True(_a1.IsExpanded);
    }

    [Fact]
    public void FilterHidesExpanderWhenNoChildSurvives()
    {
        _model.Filter = item => ((TestItem)item).Name.Length == 1;

        Assert.Equal("A,B,C", Visible());
        Assert.False(Node(_a).ShowsExpander);

        _model.Filter = null;
        Assert.True(Node(_a).ShowsExpander);
    }

    [Fact]
    public void FilterIsReappliedWhenChildrenChange()
    {
        _model.FilterLevel = FilterLevel.Extended;
        _model.ExpandFilterMatches = true;
        _model.Filter = item => ((TestItem)item).Name.Contains('x');
        Assert.Equal("", Visible());

        _b.Children.Add(new TestItem("Bx"));

        // Not auto-expanded: only setting the filter expands matches.
        Assert.Equal("B", Visible());
        Node(_b).IsExpanded = true;
        Assert.Equal("B,Bx", Visible());
    }

    [Fact]
    public void RefreshPicksUpPropertyChangesTheFilterDependsOn()
    {
        _model.FilterLevel = FilterLevel.Extended;
        _model.ExpandFilterMatches = true;
        var hidden = new HashSet<TestItem> { _c };
        _model.Filter = item => !hidden.Contains((TestItem)item);
        Assert.Equal("A,A1,A1a,A2,B,B1", Visible());

        hidden.Clear();
        _model.Refresh();

        Assert.Equal("A,A1,A1a,A2,B,B1,C", Visible());
    }

    [Fact]
    public void FlatModeListsEveryMatchingNodeWithoutIndent()
    {
        _model.FilterLevel = FilterLevel.Extended;
        _model.ExpandFilterMatches = true;
        _model.IsFlat = true;
        Assert.Equal("A,A1,A1a,A2,B,B1,C", Visible());
        Assert.Equal(0, Node(_a1a).DisplayLevel);
        Assert.False(Node(_a).ShowsExpander);

        _model.Filter = item => ((TestItem)item).Name.Contains('1');
        Assert.Equal("A1,A1a,B1", Visible());

        _model.IsFlat = false;
        Assert.Equal("A,A1,A1a,B,B1", Visible());
        Assert.Equal(2, Node(_a1a).DisplayLevel);
    }

    [Fact]
    public void AllLevelHidesTheSubtreeOfANonMatchingParentAndExpandsNothing()
    {
        _model.ExpandAll();
        _model.CollapseAll(Node(_b));

        _model.Filter = item => ((TestItem)item).Name != "A1";

        Assert.Equal("A,A2,B,C", Visible());
        Assert.False(_b.IsExpanded);
    }

    [Fact]
    public void RootLevelFiltersOnlyTopLevelRows()
    {
        _model.FilterLevel = FilterLevel.Root;
        _model.ExpandAll();

        _model.Filter = item => ((TestItem)item).Name.StartsWith('A') && ((TestItem)item).Name != "A1";

        Assert.Equal("A,A1,A1a,A2", Visible());
    }

    [Fact]
    public void ExpandCanBeCancelled()
    {
        _model.NodeExpandStateChanging += (_, e) => e.Cancel = e.Node.Item == _a;
        var raised = new List<string>();
        _model.NodeExpandStateChanged += (_, e) => raised.Add($"{e.Node.Item}:{e.IsExpanded}");

        Node(_a).IsExpanded = true;
        Node(_b).IsExpanded = true;

        Assert.Equal("A,B,B1,C", Visible());
        Assert.False(_a.IsExpanded);
        Assert.Equal(["B:True"], raised);
    }

    [Fact]
    public void FilterAndColumnPredicateMustBothPass()
    {
        _model.FilterLevel = FilterLevel.Extended;
        _model.ExpandAll();

        _model.Filter = item => ((TestItem)item).Name.StartsWith('A');
        _model.ColumnPredicate = item => ((TestItem)item).Name.Length > 2;

        Assert.Equal("A,A1,A1a", Visible());

        _model.Filter = null;
        Assert.Equal("A,A1,A1a", Visible());
        _model.ColumnPredicate = null;
        Assert.Equal("A,A1,A1a,A2,B,B1,C", Visible());
    }

    [Fact]
    public void FlatModeSortsAcrossLevels()
    {
        _model.IsFlat = true;

        _model.Sort = (x, y) => ByName(y, x);

        Assert.Equal("C,B1,B,A2,A1a,A1,A", Visible());
    }

    [Fact]
    public void LargeChangeRaisesSingleReset()
    {
        _model.Rows.MaxIncrementalChanges = 2;

        _model.ExpandAll();

        Assert.Equal("A,A1,A1a,A2,B,B1,C", Visible());
        var e = Assert.Single(_mirror.Events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, e.Action);
    }

    [Fact]
    public void ItemsWithoutSchemaPropertiesAreLeaves()
    {
        var model = new TreeModel(TreeSchema.FromPropertyNames("Children", "IsExpanded")) { ItemsSource = new[] { "x", "y" } };

        model.GetNode("x")!.IsExpanded = true;

        Assert.Equal("x,y", string.Join(",", model.Rows));
        Assert.False(model.GetNode("x")!.ShowsExpander);
    }
}
