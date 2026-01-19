using System.Text.RegularExpressions;

namespace GameData.Shared.Utils;

/// <summary>
/// Helper for correcting tier-based ability icon name misalignment.
/// </summary>
public static class AbilityIconHelper
{
    /// <summary>
    /// Applies +1 tier offset to specific tier-based ability icon names.
    ///
    /// DEMO BUILD QUIRK: Only three abilities have misaligned tier numbering:
    /// - base_passive_evasive (Ranged Defence I-IV)
    /// - base_passive_resistant (Magic Defence I-IV)
    /// - base_passive_steelskin (Melee Defence I-IV)
    ///
    /// These three "defence trio" abilities:
    /// - Have text SIDs numbered 0-3 (base_passive_XXX_0_name through _3_name)
    /// - Have icon files numbered 0-5 (but icons 0 and 5 are unused/incorrect)
    /// - Icon Roman numerals are off by one (tier 0 shows "I", tier 1 shows "II", etc.)
    /// - Need +1 offset to match: tier 0 -> icon 1, tier 1 -> icon 2, etc.
    ///
    /// ALL OTHER tier abilities are correctly numbered 1-N and must NOT receive offset:
    /// - base_passive_immunity_magic (1-5)
    /// - base_passive_aware (1-2)
    /// - base_passive_staunch (1-3)
    /// - base_passive_strike_pierce, _reach, _rumble, _swipe, _swirl, _tri_reach (1-2/1-3)
    /// </summary>
    public static string ApplyTierIconOffset(string nameSid)
    {
        if (string.IsNullOrWhiteSpace(nameSid))
            return nameSid;

        // Whitelist: ONLY the three "defence trio" abilities need +1 offset
        bool needsOffset = nameSid.Contains("evasive", StringComparison.OrdinalIgnoreCase) ||
                           nameSid.Contains("resistant", StringComparison.OrdinalIgnoreCase) ||
                           nameSid.Contains("steelskin", StringComparison.OrdinalIgnoreCase);

        if (!needsOffset)
        {
            return nameSid; // Most abilities are correctly numbered, return unchanged
        }

        // Match pattern: base_passive_XXXX_N_name where N is 0-5
        var match = Regex.Match(
            nameSid,
            @"^(base_passive_\w+)_(\d)(_name)$",
            RegexOptions.IgnoreCase
        );

        if (match.Success)
        {
            var basePart = match.Groups[1].Value;
            var tierNum = int.Parse(match.Groups[2].Value);
            var suffix = match.Groups[3].Value;

            // Apply +1 offset (tier 0 -> icon 1, tier 1 -> icon 2, etc.)
            var newTier = tierNum + 1;

            return $"{basePart}_{newTier}{suffix}";
        }

        // Doesn't match tier pattern, return unchanged
        return nameSid;
    }

    /// <summary>
    /// Applies icon name mappings for abilities where the icon file name differs from the ability SID.
    ///
    /// DEMO BUILD QUIRK: Some abilities have inconsistent naming between text SIDs and icon files.
    /// This mapping ensures the correct icon file is loaded even when naming doesn't match.
    /// </summary>
    public static string ApplyIconNameMappings(string nameSid)
    {
        if (string.IsNullOrWhiteSpace(nameSid))
            return nameSid;

        // Icon name mappings: ability SID -> icon file name (without .png extension)
        var iconNameMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "base_passive_remote_attack_penalty_name", "base_remote_attack_penalty_name" }
        };

        return iconNameMappings.TryGetValue(nameSid, out var mappedName) ? mappedName : nameSid;
    }

    /// <summary>
    /// Applies all icon name transformations (tier offset + name mappings) in the correct order.
    /// This is the recommended method to use for getting the final icon file name.
    /// </summary>
    public static string GetIconFileName(string nameSid)
    {
        if (string.IsNullOrWhiteSpace(nameSid))
            return nameSid;

        var transformed = ApplyTierIconOffset(nameSid);
        transformed = ApplyIconNameMappings(transformed);

        return transformed;
    }
}
