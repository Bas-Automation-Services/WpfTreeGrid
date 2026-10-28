using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace WpfTreeGrid.Demo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Roots = DemoNode.Generate(10_000);
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<DemoNode> Roots { get; }

    public ObservableCollection<object> Selection { get; } = [];

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text;
        Grid.RowFilter = text.Length == 0 ? null : item => ((DemoNode)item).Name.Contains(text, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnFilterChanged(object? sender, EventArgs e) =>
        FilterSummary.Text = string.Join("   ", Grid.Columns.OfType<ITreeGridColumn>()
            .Where(c => c.FilterPredicates.Count > 0)
            .Select(c => FilterPredicate.Describe(c.HeaderText ?? "", c.FilterPredicates)));

    private void OnAddPartClick(object sender, RoutedEventArgs e)
    {
        foreach (var parent in Selection.Cast<DemoNode>().ToList())
            parent.Children.Add(new DemoNode { Name = $"New part {parent.Children.Count + 1}", Type = "Part", Mass = 1 });
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        foreach (var node in Selection.Cast<DemoNode>().ToList())
            (Grid.GetNode(node)?.ParentNode?.Item is DemoNode parent ? parent.Children : Roots).Remove(node);
    }
}
