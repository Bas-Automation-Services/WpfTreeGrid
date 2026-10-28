using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfTreeGrid.Demo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Roots = DemoNode.Generate(10_000);
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => ApplyCommandLine(Environment.GetCommandLineArgs().Skip(1).ToList());
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

    /// <summary>
    /// Scripted states for screenshots: <c>--search text</c>, <c>--flat</c>, <c>--expand-all</c>,
    /// <c>--sort Header[:desc]</c>, <c>--filter Header</c> (opens the filter popup), <c>--select N</c>.
    /// </summary>
    private void ApplyCommandLine(List<string> args)
    {
        string? Arg(string name) => args.IndexOf(name) is var i and >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        DataGridColumn Column(string header) => Grid.Columns.First(c => (string)c.Header == header);

        if (args.Contains("--flat")) Grid.IsFlat = true;
        if (args.Contains("--expand-all")) Grid.ExpandAllNodes();
        if (Arg("--search") is { } search) SearchBox.Text = search;
        if (Arg("--sort") is { } sort)
        {
            var parts = sort.Split(':');
            Grid.SortBy(Column(parts[0]), parts is [_, "desc"] ? ListSortDirection.Descending : ListSortDirection.Ascending);
        }
        if (Arg("--select") is { } select)
            foreach (var item in Grid.Items.Cast<object>().Skip(1).Take(int.Parse(select)))
                Selection.Add(item);
        if (Arg("--filter") is { } filter)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                var header = FindVisual<DataGridColumnHeader>(Grid).First(h => h.Column == Column(filter));
                var button = FindVisual<ButtonBase>(header).First();
                TreeDataGrid.ShowColumnFilterCommand.Execute(header.Column, button);
            });
        }
    }

    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisual<T>(child)) yield return descendant;
        }
    }
}
