using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfTreeGrid;

/// <summary>
/// Wraps a tree column's cell content with the indent and expander of its row's
/// <see cref="TreeNode"/>. Restyle it through the implicit <c>TreeCell</c> style.
/// </summary>
public class TreeCell : ContentControl
{
    public static readonly DependencyProperty NodeProperty =
        DependencyProperty.Register(nameof(Node), typeof(TreeNode), typeof(TreeCell), new PropertyMetadata(null));

    private readonly TreeDataGrid? _grid;

    static TreeCell()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TreeCell), new FrameworkPropertyMetadata(typeof(TreeCell)));
        FocusableProperty.OverrideMetadata(typeof(TreeCell), new FrameworkPropertyMetadata(false));
    }

    public TreeCell() { }

    internal TreeCell(TreeDataGrid? grid, FrameworkElement content)
    {
        _grid = grid;
        Content = content;
        // Rows are recycled, so the node follows the inherited DataContext rather than the item the cell was built for.
        DataContextChanged += (_, e) => Node = e.NewValue is null ? null : _grid?.GetNode(e.NewValue);
    }

    public TreeNode? Node
    {
        get => (TreeNode?)GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    internal static FrameworkElement Unwrap(FrameworkElement element) =>
        element is TreeCell { Content: FrameworkElement content } ? content : element;
}

/// <summary>Indent width for a cell: <see cref="TreeNode.DisplayLevel"/> times <see cref="TreeDataGrid.Indent"/>.</summary>
public sealed class IndentConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [int level, double indent] ? level * indent : 0.0;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True to Visible, false to Hidden, so rows without an expander keep their alignment.</summary>
public sealed class BooleanToHiddenConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Hidden;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
