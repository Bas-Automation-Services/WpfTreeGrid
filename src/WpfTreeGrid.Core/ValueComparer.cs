namespace WpfTreeGrid;

/// <summary>A sort key: the member to compare and its direction.</summary>
public sealed record SortKey(MemberPath Path, bool Descending = false);

/// <summary>
/// Compares cell values for sorting: nulls first, numbers by value across numeric
/// types, strings naturally ("Part2" before "Part10") and case-insensitively, and
/// anything else through <see cref="IComparable"/> or its text.
/// </summary>
public static class ValueComparer
{
    public static Comparison<object> Create(IReadOnlyList<SortKey> keys) => (x, y) =>
    {
        foreach (var key in keys)
        {
            var result = Compare(key.Path.GetValue(x), key.Path.GetValue(y));
            if (result != 0)
                return key.Descending ? -result : result;
        }
        return 0;
    };

    public static int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        if (IsNumber(x) && IsNumber(y)) return Convert.ToDouble(x).CompareTo(Convert.ToDouble(y));
        if (x is string sx && y is string sy) return CompareNatural(sx, sy);
        if (x.GetType() == y.GetType() && x is IComparable comparable) return comparable.CompareTo(y);
        return CompareNatural(x.ToString() ?? "", y.ToString() ?? "");
    }

    /// <summary>Case-insensitive comparison that orders embedded digit runs by numeric value.</summary>
    public static int CompareNatural(string x, string y)
    {
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var digitsX = x.AsSpan(startX, i - startX).TrimStart('0');
                var digitsY = y.AsSpan(startY, j - startY).TrimStart('0');
                var result = digitsX.Length != digitsY.Length
                    ? digitsX.Length.CompareTo(digitsY.Length)
                    : digitsX.SequenceCompareTo(digitsY);
                if (result != 0) return result;
            }
            else
            {
                var result = string.Compare(x, i, y, j, 1, StringComparison.CurrentCultureIgnoreCase);
                if (result != 0) return result;
                i++;
                j++;
            }
        }
        var lengthResult = (x.Length - i).CompareTo(y.Length - j);
        return lengthResult != 0 ? lengthResult : string.CompareOrdinal(x, y);
    }

    public static bool IsNumber(object value) => IsNumberType(value.GetType());

    public static bool IsNumberType(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
        || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
}
