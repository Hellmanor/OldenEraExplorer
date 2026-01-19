using Localization.Indexing;

namespace GameData.Services;

/// <summary>
/// Centralized service for mapping class type identifiers to localized display names.
/// Handles faction-specific class names with fallback to generic class types.
/// </summary>
public class ClassMapper
{
    private LangIndex? _langIndex;
    private readonly FactionMapper _factionMapper;

    public ClassMapper(FactionMapper factionMapper)
    {
        _factionMapper = factionMapper ?? throw new ArgumentNullException(nameof(factionMapper));
    }

    public void SetLangIndex(LangIndex? langIndex)
    {
        _langIndex = langIndex;
    }

    /// <summary>
    /// Maps a class type and faction to a localized display name.
    /// First tries faction-specific class name (e.g., "might_demon_name"),
    /// then falls back to generic class type capitalization.
    /// </summary>
    /// <param name="classType">The class type (e.g., "might", "magic")</param>
    /// <param name="faction">The faction identifier (e.g., "demon", "demons", "human")</param>
    /// <returns>Localized class display name, or null if classType is null/empty</returns>
    public string? MapClassDisplay(string? classType, string? faction)
    {
        if (string.IsNullOrWhiteSpace(classType))
            return null;

        if (_langIndex is not null && !string.IsNullOrWhiteSpace(faction))
        {
            var factionSid = _factionMapper.GetFactionSid(faction);
            if (!string.IsNullOrWhiteSpace(factionSid))
            {
                // Extract normalized faction from SID: "demon_name" -> "demon"
                var normalizedFaction = factionSid.Replace("_name", "");
                var classNameSid = $"{classType}_{normalizedFaction}_name";
                var resolved = _langIndex.ResolveText(classNameSid);
                if (!string.IsNullOrWhiteSpace(resolved) && resolved != classNameSid)
                    return resolved;
            }
        }

        return classType switch
        {
            "might" => "Might",
            "magic" => "Magic",
            _ => classType
        };
    }

    /// <summary>
    /// Gets the class icon path for a given class type and faction.
    /// Handles plural forms via FactionMapper normalization.
    /// </summary>
    /// <param name="classType">The class type (e.g., "might", "magic")</param>
    /// <param name="faction">The faction identifier (e.g., "demon", "demons", "human")</param>
    /// <returns>Icon path or null if parameters are invalid</returns>
    public string? GetClassIconPath(string? classType, string? faction)
    {
        if (string.IsNullOrWhiteSpace(classType) || string.IsNullOrWhiteSpace(faction))
            return null;

        var factionSid = _factionMapper.GetFactionSid(faction);
        if (string.IsNullOrWhiteSpace(factionSid))
            return null;

        // Extract normalized faction from SID: "demon_name" -> "demon"
        var normalizedFaction = factionSid.Replace("_name", "");

        return $"icons/hero_classes/{classType.ToLower()}_{normalizedFaction}_icon";
    }
}
