using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfTreeGrid;

/// <summary>
/// The Excel-style popup behind a column's filter button. It filters either
/// by up to two conditions or, when no condition is set, by the checked values; the result is a
/// list of <see cref="FilterPredicate"/>s (checked values as
/// Equals predicates joined by Or).
/// </summary>
public partial class ColumnFilterPanel : UserControl
{
    private static readonly OperatorOption[] Operators =
    [
        new(null, false, "(none)"),
        new(FilterType.Equals, false, "equals"),
        new(FilterType.NotEquals, false, "does not equal"),
        new(FilterType.Contains, false, "contains"),
        new(FilterType.NotContains, false, "does not contain"),
        new(FilterType.StartsWith, false, "starts with"),
        new(FilterType.NotStartsWith, false, "does not start with"),
        new(FilterType.EndsWith, false, "ends with"),
        new(FilterType.NotEndsWith, false, "does not end with"),
        new(FilterType.GreaterThan, false, "is greater than"),
        new(FilterType.GreaterThanOrEqual, false, "is at least"),
        new(FilterType.LessThan, false, "is less than"),
        new(FilterType.LessThanOrEqual, false, "is at most"),
        new(FilterType.Equals, true, "is empty"),
        new(FilterType.NotEquals, true, "is not empty"),
    ];

    private readonly List<ValueOption> _options;
    private readonly Type? _valueType;
    private List<ValueOption> _shown;

    public ColumnFilterPanel(IReadOnlyList<object?> values, IReadOnlyList<FilterPredicate> current)
    {
        InitializeComponent();

        _valueType = values.FirstOrDefault(v => v is not null)?.GetType();
        var isChecklist = IsChecklist(current);
        var checkedTexts = isChecklist ? current.Select(p => FilterText.Of(p.FilterValue)).ToHashSet() : null;
        _options = values.Select(v => new ValueOption(v, checkedTexts?.Contains(FilterText.Of(v)) ?? true)).ToList();
        foreach (var option in _options)
            option.PropertyChanged += (_, _) => UpdateSelectAll();
        _shown = _options;
        ValueList.ItemsSource = _shown;
        UpdateSelectAll();

        FirstOperator.ItemsSource = Operators;
        SecondOperator.ItemsSource = Operators;
        var conditions = isChecklist ? [] : current;
        Load(conditions.ElementAtOrDefault(0), FirstOperator, FirstValue);
        Load(conditions.ElementAtOrDefault(1), SecondOperator, SecondValue);
        MatchAny.IsChecked = conditions.ElementAtOrDefault(1)?.PredicateType is PredicateType.Or or PredicateType.OrElse;

        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Raised with the column's new predicates; an empty list clears the filter.</summary>
    public event Action<IReadOnlyList<FilterPredicate>>? Applied;

    public event Action? Cancelled;

    /// <summary>Raised by the sort buttons at the top.</summary>
    public event Action<ListSortDirection>? SortRequested;

    internal void FocusFirstInput() => (_options.Count > 20 ? SearchBox : FirstValue).Focus();

    internal IReadOnlyList<FilterPredicate> BuildPredicates()
    {
        var first = Read(FirstOperator, FirstValue);
        var second = Read(SecondOperator, SecondValue);
        if (first is not null || second is not null)
        {
            var conditions = new[] { first, second }.OfType<FilterPredicate>().ToList();
            if (conditions.Count == 2 && MatchAny.IsChecked == true)
                conditions[1].PredicateType = PredicateType.Or;
            return conditions;
        }

        if (_options.All(o => o.IsChecked))
            return [];
        // "(Blanks)" stands for both null and empty text, which predicates tell apart.
        return _options.Where(o => o.IsChecked)
            .SelectMany(o => o.Text == FilterText.Blank ? new object?[] { null, "" } : [o.Value])
            .Select((value, i) => new FilterPredicate
            {
                FilterType = FilterType.Equals,
                FilterValue = value,
                FilterBehavior = value is null or string ? FilterBehavior.StringTyped : FilterBehavior.StronglyTyped,
                PredicateType = i == 0 ? PredicateType.And : PredicateType.Or,
            })
            .ToList();
    }

    private static bool IsChecklist(IReadOnlyList<FilterPredicate> predicates) =>
        predicates.Count > 0
        && predicates.All(p => p.FilterType == FilterType.Equals)
        && predicates.Skip(1).All(p => p.PredicateType is PredicateType.Or or PredicateType.OrElse);

    private static void Load(FilterPredicate? predicate, ComboBox op, TextBox value)
    {
        op.SelectedItem = predicate is null
            ? Operators[0]
            : Operators.First(o => o.Type == predicate.FilterType && o.IsBlankCheck == (predicate.FilterValue is null));
        value.Text = FilterText.Of(predicate?.FilterValue, blank: "");
    }

    private FilterPredicate? Read(ComboBox op, TextBox value)
    {
        if (op.SelectedItem is not OperatorOption { Type: { } type } option)
            return null;
        // Text conditions compare as text; comparisons by the column's value type.
        var isTextCondition = type is FilterType.Contains or FilterType.NotContains or FilterType.StartsWith
            or FilterType.NotStartsWith or FilterType.EndsWith or FilterType.NotEndsWith;
        var typed = option.IsBlankCheck ? null : isTextCondition ? value.Text : Convert(value.Text);
        return new FilterPredicate
        {
            FilterType = type,
            FilterValue = typed,
            FilterBehavior = typed is null or string ? FilterBehavior.StringTyped : FilterBehavior.StronglyTyped,
        };
    }

    /// <summary>Typed text becomes the column's value type when it parses as one, so comparisons are numeric or by date.</summary>
    private object Convert(string text)
    {
        if (_valueType is not null && ValueComparer.IsNumberType(_valueType)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number))
            return number;
        if (_valueType == typeof(DateTime) && DateTime.TryParse(text, CultureInfo.CurrentCulture, out var date))
            return date;
        return text;
    }

    private void UpdateSelectAll() => SelectAll.IsChecked =
        _shown.All(o => o.IsChecked) ? true : _shown.Any(o => o.IsChecked) ? null : false;

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var search = SearchBox.Text;
        _shown = search.Length == 0 ? _options : _options.Where(o => o.Text.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();
        ValueList.ItemsSource = _shown;
        UpdateSelectAll();
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        // A click on the indeterminate box selects all, like Excel.
        var check = SelectAll.IsChecked != false;
        foreach (var option in _shown)
            option.IsChecked = check;
        UpdateSelectAll();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.OriginalSource is not ComboBox)
        {
            Applied?.Invoke(BuildPredicates());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Cancelled?.Invoke();
            e.Handled = true;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Applied?.Invoke(BuildPredicates());

    private void OnClearClick(object sender, RoutedEventArgs e) => Applied?.Invoke([]);

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancelled?.Invoke();

    private void OnSortAscendingClick(object sender, RoutedEventArgs e) => SortRequested?.Invoke(ListSortDirection.Ascending);

    private void OnSortDescendingClick(object sender, RoutedEventArgs e) => SortRequested?.Invoke(ListSortDirection.Descending);

    private sealed record OperatorOption(FilterType? Type, bool IsBlankCheck, string Text);

    private sealed class ValueOption(object? value, bool isChecked) : INotifyPropertyChanged
    {
        private bool _isChecked = isChecked;

        public event PropertyChangedEventHandler? PropertyChanged;

        public object? Value { get; } = value;

        public string Text { get; } = FilterText.Of(value);

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }
    }
}
