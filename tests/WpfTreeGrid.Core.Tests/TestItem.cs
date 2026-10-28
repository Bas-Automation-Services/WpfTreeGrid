using System.Collections.ObjectModel;
using System.ComponentModel;

namespace WpfTreeGrid.Tests;

internal sealed class TestItem(string name, params TestItem[] children) : INotifyPropertyChanged
{
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;

    public ObservableCollection<TestItem> Children { get; } = new(children);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public override string ToString() => Name;
}
