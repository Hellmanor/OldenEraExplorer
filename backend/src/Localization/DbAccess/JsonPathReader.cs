using System.Globalization;
using System.Text.Json;

namespace Localization.DbAccess;

/// <summary>
/// Navigates JSON structures using dot-notation paths with array indexing support.
/// Examples: "stats.damageMin", "abilities[0].damage", "bonuses[1].parameters[0]"
/// </summary>
public static class JsonPathReader
{
    public static bool TryGet(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(path)) return false;

        int i = 0;
        while (i < path.Length)
        {
            int dot = path.IndexOf('.', i);
            int bracket = path.IndexOf('[', i);
            if (bracket != -1 && (dot == -1 || bracket < dot))
            {
                var key = path.Substring(i, bracket - i);
                if (!string.IsNullOrEmpty(key))
                {
                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value))
                        return false;
                }
                int close = path.IndexOf(']', bracket + 1);
                if (close == -1) return false;
                var idxStr = path.Substring(bracket + 1, close - bracket - 1);
                if (!int.TryParse(idxStr, out var idx)) return false;
                if (value.ValueKind != JsonValueKind.Array) return false;

                int c = 0;
                bool ok = false;
                foreach (var el in value.EnumerateArray())
                {
                    if (c == idx) { value = el; ok = true; break; }
                    c++;
                }
                if (!ok) return false;

                i = close + 1;
                if (i < path.Length && path[i] == '.') i++;
            }
            else
            {
                string key = dot == -1 ? path.Substring(i) : path.Substring(i, dot - i);
                if (!string.IsNullOrEmpty(key))
                {
                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value))
                        return false;
                }
                i = dot == -1 ? path.Length : dot + 1;
            }
        }
        return true;
    }

    public static string? AsString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var i64) ? i64.ToString() : el.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static double? AsDouble(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.String => double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null
        };
    }
}
