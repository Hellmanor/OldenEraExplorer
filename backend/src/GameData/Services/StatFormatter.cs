using Microsoft.Extensions.Logging;

namespace GameData.Services;

/// <summary>
/// Centralizes unit stat formatting logic.
/// Handles damage range formatting, stat value extraction, name normalization, and icon mapping.
/// </summary>
public class StatFormatter
{
    private readonly ILogger<StatFormatter>? _logger;

    public StatFormatter(ILogger<StatFormatter>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Formats damage range from damageMin and damageMax values in stat dictionary.
    /// Returns formatted string like "10 - 20" or "-" if both values are missing.
    /// </summary>
    public string FormatDamageRange(Dictionary<string, string> statDict)
    {
        statDict.TryGetValue("damageMin", out var dmin);
        statDict.TryGetValue("damageMax", out var dmax);

        if (!string.IsNullOrWhiteSpace(dmin) || !string.IsNullOrWhiteSpace(dmax))
        {
            var left = string.IsNullOrWhiteSpace(dmin) ? "-" : dmin!.Trim();
            var right = string.IsNullOrWhiteSpace(dmax) ? "-" : dmax!.Trim();
            return $"{left} - {right}";
        }

        return "-";
    }

    /// <summary>
    /// Extracts stat value from dictionary based on stat key with fallback keys.
    /// For example, "health" checks "hp" and "health", "attack" checks "attack" and "offence".
    /// </summary>
    public string ExtractStatValue(Dictionary<string, string> statDict, string statKey)
    {
        return statKey switch
        {
            "health" => TryGetAny(statDict, "hp", "health") ?? "-",
            "attack" => TryGetAny(statDict, "attack", "offence") ?? "-",
            "defence" => TryGetAny(statDict, "defence", "defense") ?? "-",
            "damage" => FormatDamageRange(statDict),
            "initiative" => TryGetAny(statDict, "initiative") ?? "-",
            "speed" => TryGetAny(statDict, "speed") ?? "-",
            "luck" => TryGetAny(statDict, "luck") ?? "-",
            "morale" => TryGetAny(statDict, "moral", "morale") ?? "-",
            _ => "-"
        };
    }

    /// <summary>
    /// Maps stat key to localization SID for overlay text lookups.
    /// </summary>
    public string GetStatLocalizationSid(string statKey)
    {
        return statKey switch
        {
            "health" => "unit_health",
            "attack" => "unit_attack",
            "defence" => "unit_defence",
            "damage" => "unit_damage",
            "initiative" => "unit_init",
            "speed" => "unit_speed",
            "luck" => "unit_luck",
            "morale" => "unit_moral",
            _ => statKey
        };
    }

    /// <summary>
    /// Maps stat key to icon name for texture lookups.
    /// </summary>
    public string GetStatIconName(string statKey)
    {
        return statKey switch
        {
            "initiative" => "unit_init",
            "morale" => "unit_moral",
            _ => $"unit_{statKey}"
        };
    }

    /// <summary>
    /// Normalizes raw stat name for display (fallback when localization fails).
    /// Handles special cases like "offence" -> "Attack", "defence" -> "Defence", etc.
    /// </summary>
    public string NormalizeStatName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "-";
        var k = raw.Trim();

        if (k.Equals("offence", StringComparison.OrdinalIgnoreCase) || k.Equals("attack", StringComparison.OrdinalIgnoreCase))
            return "Attack";
        if (k.Equals("defense", StringComparison.OrdinalIgnoreCase) || k.Equals("defence", StringComparison.OrdinalIgnoreCase))
            return "Defence";
        if (k.Equals("squadvalue", StringComparison.OrdinalIgnoreCase)) return "Squad Value";
        if (k.Equals("expbonus", StringComparison.OrdinalIgnoreCase)) return "Exp Bonus";

        if (k.Equals("health", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("initiative", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("speed", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("luck", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("morale", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("damage", StringComparison.OrdinalIgnoreCase))
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(k);

        var t = k.Replace('_', ' ').Replace('-', ' ');
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(t);
    }

    /// <summary>
    /// Tries to get value from dictionary using multiple keys (case-insensitive).
    /// Returns first non-empty value found, or null if all keys are missing or empty.
    /// </summary>
    public string? TryGetAny(Dictionary<string, string> raw, params string[] keys)
    {
        foreach (var k in keys)
            if (raw.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
        return null;
    }
}
