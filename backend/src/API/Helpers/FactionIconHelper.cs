namespace API.Helpers;

public static class FactionIconHelper
{
    // Returns null if faction is null, empty, or "neutral" (neutral has no icon)
    public static string? GetFactionIconPath(string? faction)
    {
        if (string.IsNullOrEmpty(faction))
            return null;

        // Neutral faction has no icon
        if (string.Equals(faction, "neutral", StringComparison.OrdinalIgnoreCase))
            return null;

        var iconName = GetFactionIconName(faction);
        return $"icons/fractions/{iconName}_icon";
    }

    // Some factions use different internal names than their icon names
    private static string GetFactionIconName(string factionKey)
    {
        return factionKey.ToLower() switch
        {
            "demon" => "hive",      // Hive faction uses "demon" internally
            "nature" => "spring",   // Sylvan faction uses "nature" internally
            _ => factionKey.ToLower()
        };
    }
}
