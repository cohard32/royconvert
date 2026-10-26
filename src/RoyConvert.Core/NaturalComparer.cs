namespace RoyConvert.Core;

/// <summary>
/// Orders file paths the way file managers do: digit runs compare as numbers ("scan2" before "scan10"),
/// everything else case-insensitively.
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                // Compare without leading zeros: longer means larger, then digit by digit.
                var a = x.AsSpan(startX, i - startX).TrimStart('0');
                var b = y.AsSpan(startY, j - startY).TrimStart('0');
                var byNumber = a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.SequenceCompareTo(b);
                if (byNumber != 0)
                {
                    return byNumber;
                }

                continue;
            }

            var byChar = string.Compare(x, i, y, j, 1, StringComparison.OrdinalIgnoreCase);
            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        var byRest = (x.Length - i).CompareTo(y.Length - j);
        // Equal apart from case or leading zeros: fall back to an exact comparison so the order is total.
        return byRest != 0 ? byRest : string.CompareOrdinal(x, y);
    }
}
