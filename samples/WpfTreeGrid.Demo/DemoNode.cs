using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfTreeGrid.Demo;

public sealed class DemoNode : INotifyPropertyChanged
{
    private string _name = "";
    private bool _isExpanded;
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public required string Type { get; init; }

    public string? Material { get; init; }

    public double Mass { get; init; }

    public int Quantity { get; init; } = 1;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    public ObservableCollection<DemoNode> Children { get; } = [];

    public override string ToString() => Name;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>An assembly tree of roughly <paramref name="target"/> nodes, deterministic per seed.</summary>
    public static ObservableCollection<DemoNode> Generate(int target, int seed = 7)
    {
        var random = new Random(seed);
        string[] materials = ["Steel S235", "Stainless 304", "Aluminium 6082", "ABS", null!];
        string[] parts = ["Bracket", "Plate", "Shaft", "Bolt M8", "Nut M8", "Washer", "Beam", "Cover", "Spacer", "Hinge"];
        var count = 0;

        DemoNode Assembly(int depth, string name)
        {
            count++;
            var node = new DemoNode { Name = name, Type = depth == 0 ? "Top assembly" : "Assembly" };
            var childCount = random.Next(3, 9);
            for (var i = 1; i <= childCount && count < target; i++)
            {
                node.Children.Add(depth < 4 && random.NextDouble() < 0.35
                    ? Assembly(depth + 1, $"{name}-A{i}")
                    : Part($"{parts[random.Next(parts.Length)]} {random.Next(1, 120)}"));
            }
            return node;
        }

        DemoNode Part(string name)
        {
            count++;
            var purchased = name.StartsWith("Bolt") || name.StartsWith("Nut") || name.StartsWith("Washer");
            return new DemoNode
            {
                Name = name,
                Type = purchased ? "Purchased" : random.NextDouble() < 0.3 ? "Sheet metal" : "Part",
                Material = materials[random.Next(materials.Length)],
                Mass = Math.Round(random.NextDouble() * (purchased ? 0.1 : 25), 3),
                Quantity = random.Next(1, purchased ? 24 : 4),
            };
        }

        var roots = new ObservableCollection<DemoNode>();
        for (var i = 1; count < target; i++)
            roots.Add(Assembly(0, $"ASM-{i:000}"));
        return roots;
    }
}
