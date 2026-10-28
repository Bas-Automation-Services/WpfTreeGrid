// Renders the demo offscreen and drives it through its API, writing the README images. Run from
// the repo root:
//   dotnet run --project docs/capture -c Release -- out
//   ffmpeg -framerate 15 -i out/frames/f%05d.png -vf "split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=none:diff_mode=rectangle" -loop 0 docs/images/demo.gif
// and copy out/tree.png and out/filter.png to docs/images. The pointer is drawn, not real input.
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfTreeGrid;
using WpfTreeGrid.Demo;
using Path = System.Windows.Shapes.Path;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = english;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = english;
        Thread.CurrentThread.CurrentCulture = english;
        Thread.CurrentThread.CurrentUICulture = english;
        var app = new Application();
        var win = new MainWindow
        {
            Width = 960, Height = 640, Left = -20000, Top = 0,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false, ShowInTaskbar = false,
        };
        var director = new Director(win, args[0]);
        win.ContentRendered += async (_, _) =>
        {
            try { await director.Run(); }
            catch (Exception e) { File.WriteAllText(System.IO.Path.Combine(args[0], "error.txt"), e.ToString()); }
            app.Shutdown();
        };
        app.Run(win);
    }
}

sealed class Director
{
    const double Fps = 15;
    readonly MainWindow _win;
    readonly string _out;
    readonly Grid _root = new() { Background = Brushes.White };
    readonly Canvas _overlay = new() { IsHitTestVisible = false };
    readonly Path _cursor;
    readonly Ellipse _ripple = new() { Stroke = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0)), StrokeThickness = 2, Visibility = Visibility.Hidden };
    TreeDataGrid _grid = null!;
    Point _pos = new(620, 360);
    int _frame;
    byte[]? _last;

    public Director(MainWindow win, string outDir)
    {
        _win = win;
        _out = outDir;
        Directory.CreateDirectory(System.IO.Path.Combine(_out, "frames"));
        var content = (UIElement)win.Content;
        win.Content = null;
        _root.Children.Add(content);
        _root.Children.Add(_overlay);
        _cursor = new Path
        {
            Data = Geometry.Parse("M0,0 L0,17 L4.2,13 L7.2,19.6 L9.6,18.6 L6.6,12 L12,12 Z"),
            Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round,
        };
        _overlay.Children.Add(_ripple);
        Panel.SetZIndex(_cursor, 100);
        _overlay.Children.Add(_cursor);
        win.Content = _root;
    }

    public async Task Run()
    {
        _grid = (TreeDataGrid)_win.FindName("Grid");
        Place(_pos);
        await Flush();

        // 1. Expand a sub-assembly.
        var sub = _grid.Items.Cast<DemoNode>().First(n => n.Type == "Assembly" && n.Children.Count >= 4 && !_grid.GetNode(n)!.IsExpanded);
        await Hold(1.0);
        var expander = Find<ToggleButton>(Row(sub)).First();
        await MoveTo(Center(expander), 0.9);
        await Click();
        Toggle(expander);
        await Hold(1.2);

        // 2. Sort by mass, descending: children stay under their parent.
        var massHeader = Header("Mass (kg)");
        await MoveTo(Center(massHeader) + new Vector(-12, 0), 0.9);
        await Click();
        _grid.SortBy(Column("Mass (kg)"), ListSortDirection.Ascending);
        await Hold(0.7);
        await Click();
        _grid.SortBy(Column("Mass (kg)"), ListSortDirection.Descending);
        await Hold(1.4);
        await Still("tree.png");

        // 3. Filter the Material column to one value.
        var materialColumn = Column("Material");
        var filterButton = (Button)Header("Material").Template.FindName("FilterButton", Header("Material"));
        await MoveTo(Center(filterButton), 0.9);
        await Click();
        var panel = OpenFilter(materialColumn, filterButton);
        await Hold(0.8);
        var selectAll = (CheckBox)panel.FindName("SelectAll");
        await MoveTo(Center(selectAll) + new Vector(-30, 0), 0.7);
        await Click();
        selectAll.IsChecked = false;
        selectAll.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, selectAll));
        await Hold(0.4);
        var valueList = (ListBox)panel.FindName("ValueList");
        var aluminium = Find<CheckBox>(valueList).First(c => (string)c.Content == "Aluminium 6082");
        await MoveTo(Center(aluminium) + new Vector(-40, 0), 0.6);
        await Click();
        aluminium.IsChecked = true;
        await Hold(0.6);
        await Still("filter.png");
        var ok = Find<Button>(panel).First(b => (string)b.Content == "OK");
        await MoveTo(Center(ok), 0.7);
        await Click();
        Invoke(new ButtonAutomationPeer(ok));
        await Hold(1.6);

        // 4. Edit a name.
        var nameColumn = Column("Name");
        var part = _grid.Items.Cast<DemoNode>().Where(n => n.Material == "Aluminium 6082").Skip(1).First();
        var cell = Find<DataGridCell>(Row(part)).First(c => c.Column == nameColumn);
        var text = Find<TextBlock>(cell).Last();
        await MoveTo(text.TransformToAncestor(_root).Transform(new Point(text.ActualWidth / 2, text.ActualHeight / 2)), 0.9);
        _grid.SelectedItem = part;
        _grid.CurrentCell = new DataGridCellInfo(part, nameColumn);
        await Click();
        await Hold(0.15);
        await Click();
        _grid.BeginEdit();
        await Flush();
        var box = Find<TextBox>(Find<DataGridCell>(Row(part)).First(c => c.Column == nameColumn)).First();
        box.CaretIndex = box.Text.Length;
        await Hold(0.4);
        foreach (var ch in " rev B")
        {
            box.Text += ch;
            box.CaretIndex = box.Text.Length;
            await Hold(0.13);
        }
        await Hold(0.5);
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        await MoveTo(_pos + new Vector(60, 40), 0.5);
        await Hold(2.0);

        File.WriteAllText(System.IO.Path.Combine(_out, "frames.txt"), _frame.ToString());
    }

    ColumnFilterPanel OpenFilter(DataGridColumn column, FrameworkElement target)
    {
        // The same panel the grid puts in its popup, shown on the overlay so the frame captures it.
        var values = _grid.View.AllNodes.Select(n => (object?)((DemoNode)n.Item).Material)
            .DistinctBy(v => FilterText.Of(v))
            .OrderBy(v => FilterText.Of(v), StringComparer.CurrentCulture)
            .ToList();
        var treeColumn = (ITreeGridColumn)column;
        var panel = new ColumnFilterPanel(values, treeColumn.FilterPredicates.ToList());
        var at = target.TransformToAncestor(_root).Transform(new Point(0, target.ActualHeight));
        Canvas.SetLeft(panel, Math.Min(at.X, _root.ActualWidth - 284));
        Canvas.SetTop(panel, at.Y);
        _overlay.Children.Add(panel);
        panel.Applied += predicates =>
        {
            _overlay.Children.Remove(panel);
            treeColumn.FilterPredicates.Clear();
            foreach (var p in predicates) treeColumn.FilterPredicates.Add(p);
            ((TextBlock)_win.FindName("FilterSummary")).Text = FilterPredicate.Describe(treeColumn.HeaderText ?? "", predicates);
        };
        return panel;
    }

    DataGridColumn Column(string header) => _grid.Columns.First(c => (string)c.Header == header);

    DataGridColumnHeader Header(string header) => Find<DataGridColumnHeader>(_grid).First(h => h.Column == Column(header));

    DataGridRow Row(object item) => (DataGridRow)_grid.ItemContainerGenerator.ContainerFromItem(item);

    Point Center(FrameworkElement e) => e.TransformToAncestor(_root).Transform(new Point(e.ActualWidth / 2, e.ActualHeight / 2));

    static void Toggle(ToggleButton b) => ((IToggleProvider)new ToggleButtonAutomationPeer(b)).Toggle();

    static void Invoke(AutomationPeer peer) => ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();

    void Place(Point p)
    {
        _pos = p;
        Canvas.SetLeft(_cursor, p.X);
        Canvas.SetTop(_cursor, p.Y);
    }

    async Task MoveTo(Point target, double seconds)
    {
        var from = _pos;
        var n = (int)(seconds * Fps);
        for (var i = 1; i <= n; i++)
        {
            var t = (double)i / n;
            var e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            Place(from + (target - from) * e);
            await Frame();
        }
    }

    async Task Click()
    {
        _ripple.Visibility = Visibility.Visible;
        foreach (var r in new[] { 4.0, 8, 12, 15 })
        {
            _ripple.Width = _ripple.Height = 2 * r;
            _ripple.Opacity = 1 - r / 18;
            Canvas.SetLeft(_ripple, _pos.X - r);
            Canvas.SetTop(_ripple, _pos.Y - r);
            await Frame();
        }
        _ripple.Visibility = Visibility.Hidden;
    }

    async Task Hold(double seconds)
    {
        await Frame();
        for (var i = 1; i < (int)(seconds * Fps); i++) Write(_last!);
    }

    async Task Frame()
    {
        await Flush();
        Write(Png(_root, 1));
    }

    async Task Still(string name)
    {
        _cursor.Visibility = Visibility.Hidden;
        await Flush();
        File.WriteAllBytes(System.IO.Path.Combine(_out, name), Png(_root, 2));
        _cursor.Visibility = Visibility.Visible;
        await Flush();
    }

    void Write(byte[] png)
    {
        _last = png;
        File.WriteAllBytes(System.IO.Path.Combine(_out, "frames", $"f{_frame++:00000}.png"), png);
    }

    Task Flush() => _win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;

    static byte[] Png(FrameworkElement e, double scale)
    {
        var bmp = new RenderTargetBitmap((int)(e.ActualWidth * scale), (int)(e.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var d in Find<T>(child)) yield return d;
        }
    }
}
