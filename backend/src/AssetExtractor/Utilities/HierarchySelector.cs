using AssetExtractor.Models;

namespace AssetExtractor.Utilities;

/// <summary>
/// Unit variant kind enum for prefab variant support.
/// </summary>
public enum UnitVariantKind
{
    Base,
    Level
}

/// <summary>
/// Unit variant info - parsed from unit name.
/// </summary>
public record UnitVariantInfo(string BaseName, UnitVariantKind Kind, string Suffix);

/// <summary>
/// HierarchySelector - ported methods for unit variant parsing.
/// Full hierarchy selection is now handled in AssetExtractor using AssetRipper API.
/// </summary>
public static class HierarchySelector
{
    /// <summary>
    /// Parse unit variant info from unit name.
    /// Examples:
    ///   "esquire" -> (BaseName: "esquire", Kind: Base, Suffix: "")
    ///   "esquire_2" -> (BaseName: "esquire", Kind: Level, Suffix: "2")
    /// </summary>
    public static UnitVariantInfo ParseUnitVariant(string unitName)
    {
        if (string.IsNullOrEmpty(unitName))
            return new UnitVariantInfo(unitName, UnitVariantKind.Base, "");

        // Check for level suffix (e.g., _2, _3)
        int lastUnderscore = unitName.LastIndexOf('_');
        if (lastUnderscore > 0 && lastUnderscore < unitName.Length - 1)
        {
            string suffix = unitName[(lastUnderscore + 1)..];
            string baseName = unitName[..lastUnderscore];

            // Check if suffix is a number (level variant)
            if (int.TryParse(suffix, out _))
            {
                return new UnitVariantInfo(baseName, UnitVariantKind.Level, suffix);
            }
        }

        return new UnitVariantInfo(unitName, UnitVariantKind.Base, "");
    }
}
