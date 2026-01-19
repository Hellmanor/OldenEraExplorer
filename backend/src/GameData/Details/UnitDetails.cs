using System.Globalization;
using GameData.Indexing;
using GameData.Services;
using GameData.Shared.Utils;
using Localization.Indexing;
using Localization.Resolution;
using Microsoft.Extensions.Logging;

namespace GameData.Details;

#region Record Types

public sealed record UnitCostEntry(string ResourceKey, string DisplayName, int Amount);

public sealed record StatKV(
    string Key,
    string Name,
    string Value,
    string IconName,
    IReadOnlyList<UnitCostEntry> CostEntries)
{
    public bool HasCostEntries => CostEntries.Count > 0;
    public bool ShowValue => !HasCostEntries;
}

public sealed record LocalizedKV(
    string Name,
    string NameSid,
    string IconName,
    string? Description,
    bool HasPlaceholders,
    string? ExcaptionInTooltip,
    IReadOnlyList<string> Immunities,
    IReadOnlyList<string> InfoNotes,
    bool HasMeta,
    string? Meta,
    string? AbilityType,
    bool HasAbilityType,
    string? AbilityTierText,
    bool HasAbilityTier,
    string? AbilityCostText,
    bool HasAbilityCost
)
{
    public bool HasExcaptionInTooltip => !string.IsNullOrWhiteSpace(ExcaptionInTooltip);
    public bool HasInfoNotes => InfoNotes.Count > 0;
}

public sealed record UnitDetailsVM(
    string DisplayName,
    string PortraitIconName,
    string? NarrativeExpanded,
    string FactionKey,
    string? FactionDisplay,
    int Tier,
    string TierFormatted,
    string BaseClassName,
    string BaseClassIconName,
    string? BaseClassDescription,
    string? BaseClassExcaptionInTooltip,
    string? BaseClassInfoDescription,
    IReadOnlyList<StatKV> Stats,
    IReadOnlyList<StatKV> PrimaryStats,
    IReadOnlyList<StatKV> SecondaryStats,
    IReadOnlyList<IReadOnlyList<StatKV>> StatsColumns,
    IReadOnlyList<LocalizedKV> Passives,
    IReadOnlyList<LocalizedKV> Abilities
);

public sealed record UnitDetailsResult(
    UnitDetailsVM Details,
    LocalizedKV? BaseClass,
    IReadOnlyList<LocalizedKV> Passives,
    IReadOnlyList<LocalizedKV> Abilities
);

#endregion

public class UnitDetailsService
{
    private readonly ILogger<UnitDetailsService>? _logger;

    public UnitDetailsService(ILogger<UnitDetailsService>? logger = null)
    {
        _logger = logger;
    }

    public UnitDetailsResult GetDetails(
        DbIndex.UnitRecord unit,
        LangIndex lang,
        ITextResolver resolver,
        FactionMapper factionMapper,
        StatFormatter statFormatter,
        string locale,
        CancellationToken token = default)
    {
        var ctx = new ResolutionContext(locale);

        var displayName = lang.ResolveText($"{unit.Id}_name") ?? unit.Id;
        string? narrativeExpanded = ResolveVia(resolver, $"{unit.Id}_narrativeDescription", ctx, out _);
        var factionDisplay = factionMapper.MapFactionDisplay(unit.Fraction);

        token.ThrowIfCancellationRequested();

        var passivesList = BuildPassives(unit, lang, resolver, locale, token);

        token.ThrowIfCancellationRequested();

        var abilitiesList = BuildActiveAbilities(unit, lang, resolver, locale, token);

        token.ThrowIfCancellationRequested();

        var (statsFlat, statsCol1, statsCol2) = BuildStats(unit, lang, statFormatter);

        token.ThrowIfCancellationRequested();

        var baseClass = BuildBaseClass(unit, resolver, ctx);

        string tierFormatted = $"Tier: {unit.Tier}";

        var details = new UnitDetailsVM(
            displayName,
            unit.Id,
            narrativeExpanded,
            unit.Fraction,
            factionDisplay,
            unit.Tier,
            tierFormatted,
            baseClass.Name,
            baseClass.IconName,
            baseClass.Description,
            BaseClassExcaptionInTooltip: null,
            BaseClassInfoDescription: null,
            statsFlat,
            statsCol1,
            statsCol2,
            new IReadOnlyList<StatKV>[] { statsCol1, statsCol2 },
            passivesList,
            abilitiesList
        );

        LocalizedKV? baseClassItem = null;
        if (!string.IsNullOrEmpty(baseClass.Name) && baseClass.Name != "-")
        {
            baseClassItem = new LocalizedKV(
                baseClass.Name,
                unit.BaseClassNameSid ?? "",
                baseClass.IconName,
                baseClass.Description,
                false,
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                false,
                null,
                null,
                false,
                null,
                false,
                null,
                false
            );
        }

        return new UnitDetailsResult(
            Details: details,
            BaseClass: baseClassItem,
            Passives: passivesList,
            Abilities: abilitiesList
        );
    }

    #region Passives

    private List<LocalizedKV> BuildPassives(
        DbIndex.UnitRecord unit,
        LangIndex lang,
        ITextResolver resolver,
        string locale,
        CancellationToken token)
    {
        var passivesList = new List<LocalizedKV>(unit.Passives.Count);
        var ctx = new ResolutionContext(locale);

        for (int i = 0; i < unit.Passives.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var aref = unit.Passives[i];
            var name = lang.ResolveText(aref.NameSid) ?? aref.NameSid;
            var iconName = AbilityIconHelper.ApplyTierIconOffset(aref.NameSid);

            var ctx2 = new ResolutionContext(ctx.Locale)
            {
                UnitId = unit.Id,
                AbilityIndex = i,
                IsActiveAbility = false
            };

            string? desc = null;
            bool hasPh = false;
            if (!string.IsNullOrWhiteSpace(aref.DescriptionSid))
            {
                foreach (var sid in EnumerateFallbacks(aref.DescriptionSid!))
                {
                    var tr = ResolveVia(resolver, sid, ctx2, out var stillHasPh);
                    if (!string.IsNullOrWhiteSpace(tr)) { desc = tr; hasPh = stillHasPh; break; }
                }
            }

            string? abilityType = null;
            bool hasAbilityType = false;
            if (!string.IsNullOrWhiteSpace(aref.AbilityTypeSid))
            {
                var t = ResolveVia(resolver, aref.AbilityTypeSid, ctx2, out _);
                if (!string.IsNullOrWhiteSpace(t))
                {
                    abilityType = t!;
                    hasAbilityType = true;
                }
            }

            var immLines = new List<string>();
            foreach (var immSid in aref.ImmunitySids ?? Array.Empty<string>())
            {
                var t = ResolveVia(resolver, immSid, ctx2, out _);
                if (!string.IsNullOrWhiteSpace(t)) immLines.Add(t!);
            }

            var infoNotesList = new List<string>();
            foreach (var infoSid in aref.InfoDescriptionSids ?? Array.Empty<string>())
            {
                var t = ResolveVia(resolver, infoSid, ctx2, out _);
                if (!string.IsNullOrWhiteSpace(t)) infoNotesList.Add(t!);
            }

            string? excaption = null;
            if (immLines.Count == 1)
                excaption = immLines[0];
            else if (immLines.Count > 1)
                excaption = string.Join(Environment.NewLine, immLines);

            passivesList.Add(new LocalizedKV(
                name,
                aref.NameSid,
                iconName,
                desc,
                hasPh,
                excaption,
                immLines,
                infoNotesList,
                HasMeta: false,
                Meta: null,
                abilityType,
                hasAbilityType,
                AbilityTierText: null,
                HasAbilityTier: false,
                AbilityCostText: null,
                HasAbilityCost: false));
        }

        return passivesList;
    }

    #endregion

    #region Active Abilities

    private List<LocalizedKV> BuildActiveAbilities(
        DbIndex.UnitRecord unit,
        LangIndex lang,
        ITextResolver resolver,
        string locale,
        CancellationToken token)
    {
        var abilitiesList = new List<LocalizedKV>(unit.Abilities.Count);
        var ctx = new ResolutionContext(locale);

        var alternatives = new List<(DbIndex.AbilityRef, int)>();
        var normalActives = new List<(DbIndex.AbilityRef, int)>();

        for (int i = 0; i < unit.Abilities.Count; i++)
        {
            if (unit.Abilities[i].IsAlternativeAttack)
                alternatives.Add((unit.Abilities[i], i));
            else
                normalActives.Add((unit.Abilities[i], i));
        }

        var orderedAbilities = new List<(DbIndex.AbilityRef, int)>(alternatives.Count + normalActives.Count);
        orderedAbilities.AddRange(alternatives);
        orderedAbilities.AddRange(normalActives);

        var rankTemplate = lang.ResolveText("tooltipsAbilityRank") ?? "Ability tier: {0}";
        var costTemplate = lang.ResolveText("tooltipsAbilityCost") ?? "Ability cost: {0}";

        foreach (var (aref, index) in orderedAbilities)
        {
            token.ThrowIfCancellationRequested();

            var name = lang.ResolveText(aref.NameSid) ?? aref.NameSid;
            var iconName = AbilityIconHelper.ApplyTierIconOffset(aref.NameSid);

            var ctx2 = new ResolutionContext(ctx.Locale)
            {
                UnitId = unit.Id,
                AbilityIndex = index,
                IsActiveAbility = true
            };

            string? desc = null;
            bool hasPh = false;
            if (!string.IsNullOrWhiteSpace(aref.DescriptionSid))
            {
                foreach (var sid in EnumerateFallbacks(aref.DescriptionSid!))
                {
                    var tr = ResolveVia(resolver, sid, ctx2, out var stillHasPh);
                    if (!string.IsNullOrWhiteSpace(tr)) { desc = tr; hasPh = stillHasPh; break; }
                }
            }

            string? abilityType = null;
            bool hasAbilityType = false;
            if (!string.IsNullOrWhiteSpace(aref.AbilityTypeSid))
            {
                var t = ResolveVia(resolver, aref.AbilityTypeSid, ctx2, out _);
                if (!string.IsNullOrWhiteSpace(t))
                {
                    abilityType = t!;
                    hasAbilityType = true;
                }
            }

            var immLines = new List<string>();
            foreach (var immSid in aref.ImmunitySids ?? Array.Empty<string>())
            {
                var t = ResolveVia(resolver, immSid, ctx2, out _);
                if (!string.IsNullOrWhiteSpace(t)) immLines.Add(t!);
            }

            var infoNotesList = new List<string>();
            foreach (var infoSid in aref.InfoDescriptionSids ?? Array.Empty<string>())
            {
                var t = ResolveVia(resolver, infoSid, ctx2, out _);
                if (!string.IsNullOrWhiteSpace(t)) infoNotesList.Add(t!);
            }

            var r = aref.Rank.HasValue ? aref.Rank.Value.ToString(CultureInfo.InvariantCulture) : "-";
            var e = aref.Energy.HasValue ? aref.Energy.Value.ToString(CultureInfo.InvariantCulture) : "-";
            var abilityTierText = rankTemplate.Replace("{0}", r);
            var abilityCostText = costTemplate.Replace("{0}", e);
            var meta = string.Join("\n", new[] { abilityTierText, abilityCostText });

            string? excaption = null;
            if (immLines.Count == 1)
                excaption = immLines[0];
            else if (immLines.Count > 1)
                excaption = string.Join(Environment.NewLine, immLines);

            abilitiesList.Add(new LocalizedKV(
                name,
                aref.NameSid,
                iconName,
                desc,
                hasPh,
                excaption,
                immLines,
                infoNotesList,
                HasMeta: true,
                Meta: meta,
                abilityType,
                hasAbilityType,
                abilityTierText,
                HasAbilityTier: true,
                abilityCostText,
                HasAbilityCost: true));
        }

        return abilitiesList;
    }

    #endregion

    #region Stats

    private (List<StatKV> flat, List<StatKV> col1, List<StatKV> col2) BuildStats(
        DbIndex.UnitRecord unit,
        LangIndex lang,
        StatFormatter statFormatter)
    {
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (unit.Stats != null)
        {
            foreach (var kv in unit.Stats)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key))
                    raw[kv.Key] = kv.Value ?? "-";
            }
        }

        var col1 = new List<StatKV>(8);
        var col1Order = new[] { "health", "attack", "defence", "damage", "initiative", "speed", "luck", "morale" };

        foreach (var key in col1Order)
        {
            string val = statFormatter.ExtractStatValue(raw, key);
            var sid = statFormatter.GetStatLocalizationSid(key);
            var name = lang.ResolveText(sid) ?? statFormatter.NormalizeStatName(key);
            var iconName = statFormatter.GetStatIconName(key);

            col1.Add(new StatKV(key, name, val, iconName, Array.Empty<UnitCostEntry>()));
        }

        var col2 = new List<StatKV>(4);

        var squadVal = statFormatter.TryGetAny(raw, "squadvalue", "squadValue");
        var expBonus = statFormatter.TryGetAny(raw, "expbonus", "expBonus");

        var squadName = lang.ResolveText("label_squad_value") ?? "Squad Value";
        var expName = lang.ResolveText("label_exp_bonus") ?? "Exp Bonus";
        var growthName = lang.ResolveText("tutorial_C3_name") ?? "Weekly Growth";
        var costName = lang.ResolveText("label_cost") ?? "Cost";

        col2.Add(new StatKV("squadValue", squadName, squadVal ?? "-", "squadValue", Array.Empty<UnitCostEntry>()));
        col2.Add(new StatKV("expBonus", expName, expBonus ?? "-", "expBonus", Array.Empty<UnitCostEntry>()));

        string growthText = unit.Growth?.ToString(CultureInfo.InvariantCulture) ?? "-";
        col2.Add(new StatKV("weeklyGrowth", growthName, growthText, "weeklyGrowth", Array.Empty<UnitCostEntry>()));

        string costText = "-";
        IReadOnlyList<UnitCostEntry> costEntries = Array.Empty<UnitCostEntry>();
        if (unit.Cost.Count > 0)
        {
            var costEntriesList = new List<UnitCostEntry>(unit.Cost.Count);
            foreach (var c in unit.Cost)
            {
                costEntriesList.Add(new UnitCostEntry(
                    c.ResourceKey,
                    FormatResourceName(c.ResourceKey, lang),
                    c.Amount));
            }
            costEntries = costEntriesList;
            costText = string.Join("; ", costEntries.Select(e => $"{e.DisplayName} {e.Amount}"));
        }
        col2.Add(new StatKV("cost", costName, costText, "cost", costEntries));

        var flatStats = new List<StatKV>(col1.Count + col2.Count);
        flatStats.AddRange(col1);
        flatStats.AddRange(col2);

        return (flatStats, col1, col2);
    }

    #endregion

    #region Base Class

    private (string Name, string IconName, string? Description) BuildBaseClass(
        DbIndex.UnitRecord unit,
        ITextResolver resolver,
        ResolutionContext ctx)
    {
        string baseClassName = "-";
        string baseClassIconName = "-";
        if (!string.IsNullOrWhiteSpace(unit.BaseClassNameSid))
        {
            baseClassName = ResolveVia(resolver, unit.BaseClassNameSid!, ctx, out _) ?? "-";
            baseClassIconName = unit.BaseClassNameSid!;
        }

        string? baseClassDesc = null;
        if (!string.IsNullOrWhiteSpace(unit.BaseClassDescSid))
            baseClassDesc = ResolveVia(resolver, unit.BaseClassDescSid!, ctx, out _);

        return (baseClassName, baseClassIconName, baseClassDesc);
    }

    #endregion

    #region Helper Methods

    private static string? ResolveVia(ITextResolver resolver, string sid, ResolutionContext ctx, out bool hasPlaceholders)
    {
        var text = resolver.Resolve(sid, ctx, out _);
        hasPlaceholders = !string.IsNullOrEmpty(text) && text!.Contains('{') && text.Contains('}');
        return text;
    }

    private static IEnumerable<string> EnumerateFallbacks(string sid)
    {
        yield return sid;
        if (sid.EndsWith("_alt", StringComparison.Ordinal)) yield return sid[..^4];
        if (sid.EndsWith("_alt2", StringComparison.Ordinal)) yield return sid[..^5];
        if (sid.EndsWith("_upg_alt", StringComparison.Ordinal)) yield return sid[..^8];
        if (sid.EndsWith("_upg", StringComparison.Ordinal)) yield return sid[..^4];
    }

    private string FormatResourceName(string resourceKey, LangIndex lang)
    {
        var sid = resourceKey switch
        {
            "gold" => "gold_name",
            "gemstones" => "gemstones_name",
            "crystals" => "crystals_name",
            "mercury" => "mercury_name",
            "ore" => "ore_name",
            "wood" => "wood_name",
            _ => null
        };

        if (sid != null)
        {
            var langResolved = lang.ResolveText(sid);
            if (!string.IsNullOrWhiteSpace(langResolved))
                return langResolved!;
        }

        return resourceKey switch
        {
            "gold" => "Gold",
            "gemstones" => "Gemstones",
            "crystals" => "Crystals",
            "mercury" => "Mercury",
            "ore" => "Ore",
            "wood" => "Wood",
            _ => TitleCase(resourceKey)
        };
    }

    private static string TitleCase(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "-";
        var t = s.Replace('_', ' ').Replace('-', ' ');
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(t);
    }

    #endregion
}
