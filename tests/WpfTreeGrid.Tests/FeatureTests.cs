using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfTreeGrid.Tests;

/// <summary>
/// One test per grid feature the BasAutomation add-ins use, exercised the way they use it.
/// </summary>
public class FeatureTests
{
    public sealed class ViewModel : INotifyPropertyChanged
    {
        private bool _hideMass;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool HideMass
        {
            get => _hideMass;
            set { _hideMass = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HideMass))); }
        }

        public string MassHeader => "Massa";

        public ObservableCollection<Part> Roots { get; } = [new Part("Frame", 10, new Part("Bolt", 1), new Part("Beam", 20)), new Part("Plate", 8, new Part("Nut", 0.5))];
    }

    /// <summary>Stands in for a localization extension ({loc:Loc Key}) that hands back a binding with its own Source.</summary>
    public sealed class Localized
    {
        public static Localized Instance { get; } = new();

        public string this[string key] => key == "Col_Name" ? "Naam" : key;
    }

    private static string Rows(TreeDataGrid grid) => string.Join(",", grid.Items.Cast<object>());

    private static void Bind(TreeDataGrid grid, ViewModel vm)
    {
        grid.DataContext = vm;
        grid.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static Window Show(FrameworkElement content)
    {
        var window = new Window { Content = content, Width = 700, Height = 400, Left = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        return window;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static TreeDataGrid Parse(string columns, string gridAttributes = "")
    {
        var xaml = $$"""
            <tg:TreeDataGrid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:tg="clr-namespace:WpfTreeGrid;assembly=WpfTreeGrid"
                             xmlns:t="clr-namespace:WpfTreeGrid.Tests;assembly=WpfTreeGrid.Tests"
                             ItemsSource="{Binding Roots}" ChildPropertyName="Children" {{gridAttributes}}>
                <tg:TreeDataGrid.Columns>
                    {{columns}}
                </tg:TreeDataGrid.Columns>
            </tg:TreeDataGrid>
            """;
        return (TreeDataGrid)XamlReader.Parse(xaml);
    }

    [StaFact]
    public void ColumnBindingsSeeTheGridDataContext()
    {
        // DocumentBrowserPanel: IsHidden="{Binding ShowVariesTextBox}"; a customer add-in: HeaderText="{loc:Loc Key}".
        var grid = Parse("""
            <tg:TreeGridTextColumn MappingName="Name" HeaderText="{Binding [Col_Name], Source={x:Static t:FeatureTests+Localized.Instance}}" />
            <tg:TreeGridNumericColumn MappingName="Mass" HeaderText="{Binding MassHeader}" IsHidden="{Binding HideMass}" />
            """);
        var vm = new ViewModel();
        Bind(grid, vm);

        Assert.Equal("Naam", grid.Columns[0].Header);
        Assert.Equal("Massa", grid.Columns[1].Header);
        Assert.Equal(Visibility.Visible, grid.Columns[1].Visibility);

        vm.HideMass = true;
        Assert.Equal(Visibility.Collapsed, grid.Columns[1].Visibility);
        Assert.True(((ITreeGridColumn)grid.Columns[1]).IsHidden);
    }

    [StaFact]
    public void ColumnCellStyleAddsToTheDefaultLook()
    {
        // GridColumnService: column.CellStyle = new Style(typeof(TreeGridCell)) with a ToolTip setter.
        var grid = new TreeDataGrid();
        var column = new TreeGridTextColumn { MappingName = "Name" };
        column.CellStyle = new Style(typeof(DataGridCell)) { Setters = { new Setter(ToolTipService.ToolTipProperty, new Binding("Name")) } };
        grid.Columns.Add(column);

        var late = new TreeGridTextColumn { MappingName = "Mass" };
        grid.Columns.Add(late);
        late.CellStyle = new Style(typeof(DataGridCell));

        var defaultStyle = (Style)grid.FindResource(TreeDataGrid.CellStyleKey);
        Assert.Same(defaultStyle, column.CellStyle.BasedOn);
        Assert.Same(defaultStyle, late.CellStyle.BasedOn);
    }

    [StaFact]
    public void WidthReadsInPixels()
    {
        // GridColumnService: Width = column.ActualWidth > 0 ? column.ActualWidth : column.Width.
        var vm = new ViewModel();
        var grid = Parse("""
            <tg:TreeGridTextColumn MappingName="Name" Width="120" />
            <tg:TreeGridNumericColumn MappingName="Mass" ColumnSizer="Star" />
            """);
        Bind(grid, vm);
        ITreeGridColumn name = (ITreeGridColumn)grid.Columns[0], mass = (ITreeGridColumn)grid.Columns[1];

        Assert.Equal(120, name.Width);
        Assert.True(double.IsNaN(mass.Width));

        var window = Show(grid);
        Assert.Equal(120, name.ActualWidth);
        Assert.True(mass.ActualWidth > 0);
        name.Width = 80;
        Assert.Equal(80, name.Width);
        window.Close();
    }

    [StaFact]
    public void SortColumnDescriptionsDriveAndReflectTheSort()
    {
        var vm = new ViewModel();
        var grid = Parse("""<tg:TreeGridNumericColumn MappingName="Mass" />""", "AutoExpandMode=\"AllNodesExpanded\"");
        Bind(grid, vm);

        grid.SortColumnDescriptions.Add(new SortColumnDescription { ColumnName = "Mass", SortDirection = ListSortDirection.Descending });
        Assert.Equal("Frame,Beam,Bolt,Plate,Nut", Rows(grid));
        Assert.Equal(ListSortDirection.Descending, grid.Columns[0].SortDirection);

        grid.SortColumnDescriptions.Clear();
        Assert.Equal("Frame,Bolt,Beam,Plate,Nut", Rows(grid));
        Assert.Null(grid.Columns[0].SortDirection);
    }

    [StaFact]
    public void ClickingAHeaderSortsAscendingThenDescending()
    {
        var vm = new ViewModel();
        var grid = Parse("""<tg:TreeGridTextColumn MappingName="Name" />""", "AutoExpandMode=\"AllNodesExpanded\"");
        Bind(grid, vm);
        var window = Show(grid);
        var header = Descendants<DataGridColumnHeader>(grid).First(h => h.Column == grid.Columns[0]);
        // The header's own click handler, as a mouse click runs it.
        var onClick = typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        void Click() => onClick.Invoke(header, null);

        Click();
        Assert.Equal("Frame,Beam,Bolt,Plate,Nut", Rows(grid));
        Assert.Equal(ListSortDirection.Ascending, grid.SortColumnDescriptions.Single().SortDirection);

        Click();
        Assert.Equal("Plate,Nut,Frame,Bolt,Beam", Rows(grid));
        Assert.Equal(ListSortDirection.Descending, grid.SortColumnDescriptions.Single().SortDirection);
        window.Close();
    }

    [StaFact]
    public void HeadersCanBeResizedAndSortingCanBeTurnedOffPerColumn()
    {
        var vm = new ViewModel();
        var grid = Parse("""
            <tg:TreeGridTextColumn MappingName="Name" />
            <tg:TreeGridTemplateColumn HeaderText="" Width="28" AllowFiltering="False" AllowSorting="False" AllowResizing="False" />
            """, "AllowResizingColumns=\"True\"");
        Bind(grid, vm);
        var window = Show(grid);

        var header = Descendants<DataGridColumnHeader>(grid).First(h => h.Column == grid.Columns[0]);
        Assert.IsType<Thumb>(header.Template.FindName("PART_RightHeaderGripper", header));
        Assert.True(grid.Columns[0].CanUserResize);
        Assert.False(grid.Columns[1].CanUserResize);
        Assert.False(grid.Columns[1].CanUserSort);
        window.Close();
    }

    [StaFact]
    public void FilterPopupAppliesConditionsAndRaisesFilterChanged()
    {
        var vm = new ViewModel();
        var grid = Parse("""<tg:TreeGridTextColumn MappingName="Name" />""", "AllowFiltering=\"True\" AutoExpandMode=\"AllNodesExpanded\"");
        Bind(grid, vm);
        var window = Show(grid);
        var changes = 0;
        grid.FilterChanged += (_, _) => changes++;

        TreeDataGrid.ShowColumnFilterCommand.Execute(grid.Columns[0], grid);
        var panel = (ColumnFilterPanel)grid.FilterPopup!.Child;
        panel.FirstOperator.SelectedIndex = panel.FirstOperator.Items.Cast<object>().ToList().FindIndex(o => o.ToString()!.Contains("starts with"));
        panel.FirstValue.Text = "F";
        var ok = Descendants<Button>(panel).Single(b => (string)b.Content == "OK");
        ok.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.Equal(1, changes);
        Assert.False(grid.FilterPopup.IsOpen);
        Assert.Equal("Frame", Rows(grid));
        Assert.Equal("starts with 'F'", ((ITreeGridColumn)grid.Columns[0]).FilterPredicates.Single().ToString());
        window.Close();
    }

    [StaFact]
    public void CheckBoxColumnTogglesWithOneClickWhenEditable()
    {
        var vm = new ViewModel();
        var grid = Parse("""
            <tg:TreeGridTextColumn MappingName="Name" />
            <tg:TreeGridCheckBoxColumn MappingName="IsSelected" />
            """, "AllowEditing=\"True\"");
        Bind(grid, vm);
        var window = Show(grid);

        var box = Descendants<CheckBox>(grid).First();
        Assert.True(box.IsHitTestVisible);
        box.IsChecked = true;
        Assert.True(vm.Roots[0].IsSelected);
        window.Close();
    }

    [StaFact]
    public void TemplateColumnShowsItsEditTemplateWhileEditing()
    {
        // CopyDesign: TreeGridTemplateColumn with CellTemplate and EditTemplate on the expander column.
        var vm = new ViewModel();
        var grid = Parse("""
            <tg:TreeGridTemplateColumn MappingName="Name">
                <tg:TreeGridTemplateColumn.CellTemplate>
                    <DataTemplate><TextBlock Text="{Binding Name}" /></DataTemplate>
                </tg:TreeGridTemplateColumn.CellTemplate>
                <tg:TreeGridTemplateColumn.EditTemplate>
                    <DataTemplate><TextBox Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}" /></DataTemplate>
                </tg:TreeGridTemplateColumn.EditTemplate>
            </tg:TreeGridTemplateColumn>
            """, "AllowEditing=\"True\"");
        Bind(grid, vm);
        var window = Show(grid);
        Assert.Empty(Descendants<TextBox>(grid));
        Assert.NotEmpty(Descendants<TreeCell>(grid));

        grid.CurrentCell = new DataGridCellInfo(vm.Roots[1], grid.Columns[0]);
        grid.BeginEdit();
        grid.UpdateLayout();
        var editor = Descendants<TextBox>(grid).Single();
        editor.Text = "Base plate";
        grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        Assert.Equal("Base plate", vm.Roots[1].Name);
        window.Close();
    }

    [StaFact]
    public void RowStyleTriggersSeeTheItem()
    {
        // ModelQuery: RowStyle DataTrigger on IsContextOnly sets Opacity and FontStyle.
        var vm = new ViewModel();
        var grid = Parse("""<tg:TreeGridTextColumn MappingName="Name" />""");
        grid.RowStyle = new Style(typeof(DataGridRow))
        {
            Triggers = { new DataTrigger { Binding = new Binding("Name"), Value = "Plate", Setters = { new Setter(UIElement.OpacityProperty, 0.5) } } },
        };
        Bind(grid, vm);
        var window = Show(grid);

        var opacities = Descendants<DataGridRow>(grid).ToDictionary(r => ((Part)r.Item).Name, r => r.Opacity);
        Assert.Equal(0.5, opacities["Plate"]);
        Assert.Equal(1.0, opacities["Frame"]);
        window.Close();
    }

    [StaFact]
    public void ExpandAndCollapseSelectedNodesWorkOnSubtrees()
    {
        var vm = new ViewModel();
        var grid = Parse("""<tg:TreeGridTextColumn MappingName="Name" />""", "SelectionMode=\"Extended\"");
        Bind(grid, vm);

        grid.SelectedItems.Add(vm.Roots[0]);
        grid.ExpandSelectedNodes();
        Assert.Equal("Frame,Bolt,Beam,Plate", Rows(grid));

        grid.CollapseSelectedNodes();
        Assert.Equal("Frame,Plate", Rows(grid));
    }

    [StaFact]
    public void SelectedItemBindsTwoWay()
    {
        // FilterableTreeGrid / CopyDesign: SelectedItem="{Binding ..., Mode=TwoWay}".
        var vm = new ViewModel();
        var grid = Parse("""<tg:TreeGridTextColumn MappingName="Name" />""");
        var holder = new SelectionHolder();
        Bind(grid, vm);
        grid.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SelectionHolder.Selected)) { Source = holder, Mode = BindingMode.TwoWay });

        grid.SelectedItem = vm.Roots[1];
        Assert.Same(vm.Roots[1], holder.Selected);

        holder.Selected = vm.Roots[0];
        Assert.Same(vm.Roots[0], grid.SelectedItem);
    }

    [StaFact]
    public void SelfRelationRootValueWithoutParentPropertyKeepsTheNestedTree()
    {
        // ModelQuery: ChildPropertyName="Children" plus SelfRelationRootValue="-1", no ParentPropertyName.
        var vm = new ViewModel();
        var grid = Parse("""<tg:TreeGridTextColumn MappingName="Name" />""", "SelfRelationRootValue=\"-1\" AutoExpandMode=\"RootNodesExpanded\" ColumnSizer=\"Auto\"");
        Bind(grid, vm);

        Assert.Equal("Frame,Bolt,Beam,Plate,Nut", Rows(grid));
    }

    [StaFact]
    public void ReplacingColumnsAtRuntimeMovesTheExpander()
    {
        // FilterableTreeGrid / OccurrencesGridControl: DynamicColumns clear and re-add columns on a live grid.
        var vm = new ViewModel();
        var grid = Parse("""<tg:TreeGridTextColumn MappingName="Name" />""", "AutoExpandMode=\"RootNodesExpanded\"");
        Bind(grid, vm);
        var window = Show(grid);

        grid.Columns.Clear();
        grid.Columns.Add(new TreeGridNumericColumn { MappingName = "Mass" });
        grid.Columns.Add(new TreeGridTextColumn { MappingName = "Name" });
        grid.UpdateLayout();

        var massCells = Descendants<DataGridCell>(grid).Where(c => c.Column == grid.Columns[0]).ToList();
        Assert.NotEmpty(massCells);
        Assert.All(massCells, c => Assert.NotEmpty(Descendants<TreeCell>(c)));
        window.Close();
    }

    [StaFact]
    public void DateTimeColumnShowsShortDates()
    {
        var column = new TreeGridDateTimeColumn { MappingName = "Modified" };

        Assert.Equal("{0:d}", ((Binding)column.Binding).StringFormat);
        column.CustomPattern = "yyyy-MM-dd";
        Assert.Equal("{0:yyyy-MM-dd}", ((Binding)column.Binding).StringFormat);
    }

    [StaFact]
    public void ViewFilterAppliesAgainAfterRefreshFilter()
    {
        // CopyDesign / DocumentBrowserPanel: View.Filter = predicate; View.RefreshFilter().
        var vm = new ViewModel();
        var grid = Parse("""<tg:TreeGridTextColumn MappingName="Name" />""");
        Bind(grid, vm);
        var hidden = new HashSet<string> { "Plate" };

        grid.View.Filter = item => !hidden.Contains(((Part)item).Name);
        grid.View.RefreshFilter();
        Assert.Equal("Frame", Rows(grid));

        hidden.Clear();
        grid.View.RefreshFilter();
        Assert.Equal("Frame,Plate", Rows(grid));
    }

    public sealed class SelectionHolder : INotifyPropertyChanged
    {
        private object? _selected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public object? Selected
        {
            get => _selected;
            set { _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
        }
    }
}
