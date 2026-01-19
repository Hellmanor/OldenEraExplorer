using System.Text.RegularExpressions;

namespace API.Utilities;

// Natural (numeric-aware) string comparer for sorting strings like demon_hero_1, demon_hero_2, ..., demon_hero_10 correctly
public sealed partial class NaturalStringComparer : IComparer<string>
{
    private readonly bool _ignoreCase;

    [GeneratedRegex(@"(\d+)", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();

    public NaturalStringComparer(bool ignoreCase = true)
    {
        _ignoreCase = ignoreCase;
    }

    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        var comparison = _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // Split both strings into parts (text and numbers)
        var xParts = SplitIntoNaturalParts(x);
        var yParts = SplitIntoNaturalParts(y);

        int minLength = Math.Min(xParts.Count, yParts.Count);

        for (int i = 0; i < minLength; i++)
        {
            var xPart = xParts[i];
            var yPart = yParts[i];

            // If both parts are numeric, compare numerically
            bool xIsNum = long.TryParse(xPart, out var xNum);
            bool yIsNum = long.TryParse(yPart, out var yNum);

            if (xIsNum && yIsNum)
            {
                int numCompare = xNum.CompareTo(yNum);
                if (numCompare != 0)
                {
                    return numCompare;
                }
            }
            else
            {
                int strCompare = string.Compare(xPart, yPart, comparison);
                if (strCompare != 0) return strCompare;
            }
        }

        return xParts.Count.CompareTo(yParts.Count);
    }

    private static List<string> SplitIntoNaturalParts(string str)
    {
        var parts = new List<string>();
        var matches = NumberRegex().Matches(str);

        if (matches.Count == 0)
        {
            parts.Add(str);
            return parts;
        }

        int lastIndex = 0;
        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                parts.Add(str.Substring(lastIndex, match.Index - lastIndex));
            }

            parts.Add(match.Value);

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < str.Length)
        {
            parts.Add(str.Substring(lastIndex));
        }

        return parts;
    }
}
