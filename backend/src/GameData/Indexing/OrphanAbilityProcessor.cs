using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Localization.Indexing;
using Localization.Resolution;

namespace GameData.Indexing;

/// <summary>
/// Result of orphan ability processing containing orphan IDs, ability-to-units mapping, and ability names.
/// </summary>
/// <param name="OrphanAbilityIds">List of orphan ability IDs (abilities not assigned to any unit/hero).</param>
/// <param name="AbilityToUnitsMap">Mapping of ability name SID to list of units that use it.</param>
/// <param name="AbilityNames">Mapping of ability name SID to resolved display name.</param>
/// <param name="AbilityDescriptions">Mapping of ability name SID to resolved description text.</param>
/// <param name="AbilityDescriptionSids">Mapping of ability name SID to description SID.</param>
public record OrphanAbilityResult(
    List<string> OrphanAbilityIds,
    Dictionary<string, List<string>> AbilityToUnitsMap,
    Dictionary<string, string> AbilityNames,
    Dictionary<string, string> AbilityDescriptions,
    Dictionary<string, string> AbilityDescriptionSids
);

/// <summary>
/// Processes orphan abilities (abilities with text data but not assigned to any unit/hero).
/// Identifies abilities not referenced by any unit, builds ability-to-units mapping, and resolves ability display names.
/// </summary>
public class OrphanAbilityProcessor
{
    private readonly ILogger<OrphanAbilityProcessor> _logger;
    private readonly string _extractedAssetsDir;

    public OrphanAbilityProcessor(ILogger<OrphanAbilityProcessor> logger, string extractedAssetsDir)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _extractedAssetsDir = extractedAssetsDir ?? throw new ArgumentNullException(nameof(extractedAssetsDir));
    }

    public OrphanAbilityResult ProcessOrphanAbilities(
        LangIndex langIndex,
        ITextResolver resolverFacade,
        HashSet<string> assignedAbilitySids,
        HashSet<string> assignedAbilityIcons)
    {
        if (langIndex == null) throw new ArgumentNullException(nameof(langIndex));
        if (resolverFacade == null) throw new ArgumentNullException(nameof(resolverFacade));
        if (assignedAbilitySids == null) throw new ArgumentNullException(nameof(assignedAbilitySids));
        if (assignedAbilityIcons == null) throw new ArgumentNullException(nameof(assignedAbilityIcons));

        var orphanIds = new List<string>();
        var abilityToUnitsMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var abilityNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var abilityDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var abilityDescriptionSids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var allAbilitySids = langIndex.AllEntries()
            .Where(e => e.Entry.Category == "unitsAbility")
            .Select(e => e.Sid)
            .ToList();

        // Only process base descriptions to avoid duplicates from variants (_upg, _alt, _upg_alt)
        var descriptionSids = allAbilitySids
            .Where(sid => sid.Contains("description", StringComparison.OrdinalIgnoreCase))
            .Where(sid => !sid.Contains("narrativeDescription", StringComparison.OrdinalIgnoreCase))
            .Where(sid => !sid.EndsWith("_upg", StringComparison.OrdinalIgnoreCase))
            .Where(sid => !sid.EndsWith("_alt", StringComparison.OrdinalIgnoreCase))
            .Where(sid => !sid.EndsWith("_upg_alt", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var descSid in descriptionSids)
        {
            var nameSids = FindAllNameSidsForDescription(descSid, langIndex);

            foreach (var nameSid in nameSids)
            {
                if (assignedAbilitySids.Contains(nameSid))
                    continue;

                // DEMO BUILD QUIRK: Skip incomplete/duplicate abilities
                if (ShouldSkipAbility(nameSid))
                    continue;

                var ctx = new ResolutionContext(langIndex.Locale);
                var name = resolverFacade.Resolve(nameSid, ctx, out _) ?? nameSid;

                var description = ResolveDescription(descSid, resolverFacade, langIndex, ctx);
                var abilityId = $"orphan__{nameSid}";
                orphanIds.Add(abilityId);
                abilityNames[nameSid] = name;
                abilityDescriptions[nameSid] = description;
                abilityDescriptionSids[nameSid] = descSid;

                // Tier abilities have offset icons that need tracking to prevent duplicate orphans
                var iconWithOffset = ApplyTierIconOffset(nameSid);
                if (iconWithOffset != nameSid)
                {
                    assignedAbilityIcons.Add(iconWithOffset);
                }

                _logger.LogDebug("Found orphan ability: {AbilityId} (name: {Name})", abilityId, name);
            }
        }

        var iconOnlyOrphans = ProcessIconsWithoutTextData(assignedAbilitySids, assignedAbilityIcons);
        orphanIds.AddRange(iconOnlyOrphans);

        _logger.LogInformation("Processed {OrphanCount} orphan abilities ({TextOrphans} with text, {IconOnlyOrphans} icon-only)",
            orphanIds.Count, orphanIds.Count - iconOnlyOrphans.Count, iconOnlyOrphans.Count);

        return new OrphanAbilityResult(orphanIds, abilityToUnitsMap, abilityNames, abilityDescriptions, abilityDescriptionSids);
    }

    /// <summary>
    /// Applies tier icon offset to ability name SID.
    /// For tier abilities (ending with _0, _1, _2, etc.), converts to icon naming convention.
    /// </summary>
    public static string ApplyTierIconOffset(string nameSid)
    {
        if (string.IsNullOrWhiteSpace(nameSid))
            return nameSid;

        // Pattern: *_0_name, *_1_name, etc. -> *_0, *_1, etc.
        if (nameSid.EndsWith("_name", StringComparison.OrdinalIgnoreCase))
        {
            var withoutName = nameSid.Substring(0, nameSid.Length - 5);
            if (withoutName.Length >= 2)
            {
                var lastUnderscore = withoutName.LastIndexOf('_');
                if (lastUnderscore > 0)
                {
                    var suffix = withoutName.Substring(lastUnderscore + 1);
                    if (int.TryParse(suffix, out _))
                    {
                        return withoutName;
                    }
                }
            }
        }

        return nameSid;
    }

    /// <summary>
    /// Finds ALL corresponding name SIDs for a description SID.
    /// Handles tier abilities that share the same description (e.g., Magic Defence I-IV all use base_passive_resistant_description).
    /// </summary>
    private List<string> FindAllNameSidsForDescription(string descSid, LangIndex langIndex)
    {
        var result = new List<string>();

        // Fallback strategy: direct replacement, progressive suffix removal, tier-based, no-suffix
        var nameSid = descSid.Replace("description", "name", StringComparison.OrdinalIgnoreCase);
        if (langIndex.ResolveText(nameSid) != null)
        {
            result.Add(nameSid);
            return result;
        }

        if (descSid.Contains("_description", StringComparison.OrdinalIgnoreCase))
        {
            var descIndex = descSid.IndexOf("_description", StringComparison.OrdinalIgnoreCase);
            if (descIndex > 0)
            {
                var basePart = descSid.Substring(0, descIndex);
                var suffixPart = descSid.Substring(descIndex + "_description".Length);
                var suffixesToTry = new List<string>();

                if (suffixPart.Equals("_upg_alt", StringComparison.OrdinalIgnoreCase))
                {
                    suffixesToTry.Add("_name_upg_alt");
                    suffixesToTry.Add("_name_upg");
                    suffixesToTry.Add("_name");
                }
                else if (suffixPart.Equals("_upg", StringComparison.OrdinalIgnoreCase))
                {
                    suffixesToTry.Add("_name_upg");
                    suffixesToTry.Add("_name");
                }
                else if (suffixPart.Equals("_alt", StringComparison.OrdinalIgnoreCase))
                {
                    suffixesToTry.Add("_name_alt");
                    suffixesToTry.Add("_name");
                }
                else
                {
                    suffixesToTry.Add("_name");
                }

                foreach (var suffix in suffixesToTry)
                {
                    nameSid = basePart + suffix;
                    if (langIndex.ResolveText(nameSid) != null)
                    {
                        result.Add(nameSid);
                        return result;
                    }
                }

                // Tier-based names (base_passive_steelskin_0_name, _1_name, etc.)
                for (int tier = 0; tier <= 5; tier++)
                {
                    nameSid = $"{basePart}_{tier}_name";
                    if (langIndex.ResolveText(nameSid) != null)
                    {
                        result.Add(nameSid);
                    }
                }

                if (result.Count > 0)
                    return result;

                // No _name suffix (base_class_living pattern)
                nameSid = basePart;
                if (langIndex.ResolveText(nameSid) != null)
                {
                    result.Add(nameSid);
                    return result;
                }
            }
        }

        return result;
    }

    private string ResolveDescription(string descSid, ITextResolver resolverFacade, LangIndex langIndex, ResolutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(descSid))
            return "";

        var resolved = resolverFacade.Resolve(descSid, ctx, out _);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        return langIndex.ResolveText(descSid) ?? "";
    }

    private bool ShouldSkipAbility(string nameSid)
    {
        // DEMO BUILD QUIRK: Advanced Fighting Style abilities have text data but no icon files
        var knownAdvancedFightingStyleAbilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "trogl_ability_1_advanced_name",
            "assassin_ability_1_advanced_name",
            "blade_dancer_ability_1_advanced_name",
            "minos_ability_1_advanced_name",
            "medusa_ability_1_advanced_name",
            "hydra_ability_1_advanced_name",
            "black_dragon_ability_1_advanced_name"
        };

        // DEMO BUILD QUIRK: Duplicate orphan abilities
        var knownDuplicateOrphanAbilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "base_passive_double_strike_ranged_name",
            "base_passive_strike_swipe_name",
            "base_passive_strike_swirl_name",
            "base_ranged_attack_name",
            "base_sharpshooter_name"
        };

        // DEMO BUILD QUIRK: Common attack animations (not actual abilities)
        var commonAttackAnimations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "common_attack_1_name",
            "common_attack_2_name",
            "common_attack_3_name"
        };

        // Additional orphan abilities to skip (tier 4-5, variants, typos, etc.)
        var additionalOrphansToSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Tier 4-5 orphans (game only uses tier 0-3)
            "base_passive_evasive_4_name",
            "base_passive_evasive_5_name",
            "base_passive_resistant_4_name",
            "base_passive_resistant_5_name",
            "base_passive_steelskin_4_name",
            "base_passive_steelskin_5_name",

            // Variant suffixes orphans
            "godslayer_passive_1_name_upg",

            // Numbered variants and typos
            "base_magical_beast_name",
            "base_passiv_etrident_strike_name",
            "base_passive_strike_swirl_2_name",
            "base_remote_attack_penalty_name",
            "troglodyte_ability_1_name"
        };

        return knownAdvancedFightingStyleAbilities.Contains(nameSid) ||
               knownDuplicateOrphanAbilities.Contains(nameSid) ||
               commonAttackAnimations.Contains(nameSid) ||
               additionalOrphansToSkip.Contains(nameSid);
    }

    /// <summary>
    /// Processes ability icons that have no corresponding text data or are not assigned to units.
    /// This ensures ALL ability icons appear in the list, even if they lack full text data.
    /// </summary>
    private List<string> ProcessIconsWithoutTextData(HashSet<string> assignedAbilitySids, HashSet<string> assignedAbilityIcons)
    {
        var iconOnlyOrphans = new List<string>();
        var iconDirectory = FindAbilityIconDirectory();
        if (iconDirectory == null)
        {
            _logger.LogWarning("Could not find ability icon directory");
            return iconOnlyOrphans;
        }

        var iconFiles = System.IO.Directory.GetFiles(iconDirectory, "*.png")
            .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
            .ToList();

        var processedIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        processedIcons.UnionWith(assignedAbilitySids);
        processedIcons.UnionWith(assignedAbilityIcons);

        foreach (var iconName in iconFiles)
        {
            if (processedIcons.Contains(iconName))
                continue;

            // Check if this is a base class duplicate icon with different naming
            // Examples: base_passive_living_name vs base_class_living
            //           base_demon_name vs base_class_demon
            bool isBaseClassDuplicate = false;
            string? baseClassEquivalent = null;

            if (iconName.StartsWith("base_passive_", StringComparison.OrdinalIgnoreCase) &&
                iconName.EndsWith("_name", StringComparison.OrdinalIgnoreCase))
            {
                // base_passive_living_name → base_class_living
                var middle = iconName.Substring("base_passive_".Length, iconName.Length - "base_passive_".Length - "_name".Length);
                baseClassEquivalent = $"base_class_{middle}";
            }
            else if (iconName.StartsWith("base_", StringComparison.OrdinalIgnoreCase) &&
                     iconName.EndsWith("_name", StringComparison.OrdinalIgnoreCase) &&
                     !iconName.StartsWith("base_class_", StringComparison.OrdinalIgnoreCase))
            {
                // base_demon_name → base_class_demon
                var middle = iconName.Substring("base_".Length, iconName.Length - "base_".Length - "_name".Length);
                baseClassEquivalent = $"base_class_{middle}";
            }

            if (baseClassEquivalent != null && processedIcons.Contains(baseClassEquivalent))
            {
                isBaseClassDuplicate = true;
                _logger.LogDebug("Skipping base class duplicate icon: {IconName} (equivalent: {BaseClassEquivalent})",
                    iconName, baseClassEquivalent);
            }

            if (isBaseClassDuplicate)
                continue;

            if (ShouldSkipAbility(iconName))
            {
                _logger.LogDebug("Skipping blacklisted icon-only orphan: {IconName}", iconName);
                continue;
            }

            var abilityId = $"orphan__{iconName}";
            iconOnlyOrphans.Add(abilityId);
            processedIcons.Add(iconName);
            assignedAbilityIcons.Add(iconName);

            _logger.LogDebug("Found icon-only orphan: {AbilityId}", abilityId);
        }

        return iconOnlyOrphans;
    }

    /// <summary>
    /// Finds the ability icon directory by searching in ExtractedAssets structure.
    /// Uses the same strategy as TexturePathResolver: searches in Assets-* directories.
    /// </summary>
    private string? FindAbilityIconDirectory()
    {
        if (!System.IO.Directory.Exists(_extractedAssetsDir))
        {
            _logger.LogWarning("ExtractedAssets directory does not exist: {Directory}", _extractedAssetsDir);
            return null;
        }

        try
        {
            var assetDirs = System.IO.Directory.GetDirectories(_extractedAssetsDir, "Assets-*")
                .OrderByDescending(d => d)
                .ToList();

            // Path: Assets-*/Assets/Resources/icons/abilities
            foreach (var assetDir in assetDirs)
            {
                var iconPath = System.IO.Path.Combine(assetDir, "Assets", "Resources", "icons", "abilities");
                if (System.IO.Directory.Exists(iconPath))
                {
                    _logger.LogDebug("Found ability icon directory: {Directory}", iconPath);
                    return iconPath;
                }
            }

            _logger.LogWarning("Could not find ability icon directory in any Assets-* folder");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning ExtractedAssets directories");
            return null;
        }
    }
}
