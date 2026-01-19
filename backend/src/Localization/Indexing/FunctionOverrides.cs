using System.Text.Json;

namespace Localization.Indexing;

/// <summary>
/// User-provided overrides for script functions that can't be auto-calculated.
/// Location: %AppData%\OE.Explorer\function-overrides.json
/// Allows users to manually specify values when runtime data is unavailable.
/// Example: { "current_day_1_magic_healing_water": "25", "some_runtime_value": "N/A" }
/// </summary>
public sealed class FunctionOverrides
{
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public int Count => _map.Count;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "OE.Explorer", "function-overrides.json");

    public FunctionOverrides(string? path = null)
    {
        var p = path ?? DefaultPath;
        try
        {
            if (File.Exists(p))
            {
                var json = File.ReadAllText(p);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    foreach (var kv in dict)
                        _map[kv.Key] = kv.Value;
                }
            }
        }
        catch
        {
        }
    }

    public string? TryGet(string funcName)
        => string.IsNullOrWhiteSpace(funcName) ? null
           : (_map.TryGetValue(funcName, out var v) ? v : null);
}
