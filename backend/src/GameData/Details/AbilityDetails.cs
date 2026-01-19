using System.Globalization;
using GameData.Indexing;
using GameData.Shared.Utils;
using Localization.Indexing;
using Localization.Resolution;
using Microsoft.Extensions.Logging;

namespace GameData.Details;

public sealed record AbilityDetailsResult(
    string AbilityId,
    string Name,
    string? Description,
    string AbilityType,
    string IconPath,
    string? AbilityTypeSid,
    int? Rank,
    int? EnergyCost,
    IReadOnlyList<string> Immunities,
    IReadOnlyList<string> InfoNotes,
    IReadOnlyList<AbilityUnitUsage> UsedByUnits
);

public sealed record AbilityUnitUsage(
    string UnitId,
    string UnitName,
    string AbilityType
);

public class AbilityDetailsService
{
    private readonly ILogger<AbilityDetailsService>? _logger;

    public AbilityDetailsService(ILogger<AbilityDetailsService>? logger = null)
    {
        _logger = logger;
    }

    public AbilityDetailsResult? GetDetails(
        AbilityIndex.AbilityRecord ability,
        LangIndex lang,
        ITextResolver resolver,
        DbIndex? dbIndex,
        string locale,
        CancellationToken token = default)
    {
        if (ability == null)
            return null;

        var ctx = new ResolutionContext(locale);

        var name = ability.NameSid != null
            ? (resolver.Resolve(ability.NameSid, ctx, out _) ?? lang.ResolveText(ability.NameSid) ?? ability.NameSid)
            : ability.AbilityId;

        token.ThrowIfCancellationRequested();

        string? description = null;
        if (!string.IsNullOrWhiteSpace(ability.DescriptionSid))
        {
            foreach (var sid in EnumerateFallbacks(ability.DescriptionSid!))
            {
                var tr = resolver.Resolve(sid, ctx, out _);
                if (!string.IsNullOrWhiteSpace(tr))
                {
                    description = tr;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(description))
                description = lang.ResolveText(ability.DescriptionSid);
        }

        token.ThrowIfCancellationRequested();

        var immunities = new List<string>();
        foreach (var immSid in ability.ImmunitySids)
        {
            var t = resolver.Resolve(immSid, ctx, out _) ?? lang.ResolveText(immSid);
            if (!string.IsNullOrWhiteSpace(t))
                immunities.Add(t!);
        }

        var infoNotes = new List<string>();
        foreach (var infoSid in ability.InfoDescriptionSids)
        {
            var t = resolver.Resolve(infoSid, ctx, out _) ?? lang.ResolveText(infoSid);
            if (!string.IsNullOrWhiteSpace(t))
                infoNotes.Add(t!);
        }

        token.ThrowIfCancellationRequested();

        var usedByUnits = new List<AbilityUnitUsage>();
        if (dbIndex != null && !string.IsNullOrWhiteSpace(ability.NameSid))
        {
            foreach (var unit in dbIndex.LoadUnits())
            {
                token.ThrowIfCancellationRequested();

                var unitName = resolver.Resolve($"{unit.Id}_name", ctx, out _)
                    ?? lang.ResolveText($"{unit.Id}_name")
                    ?? unit.Id;

                if (ability.NameSid.Equals(unit.BaseClassNameSid, StringComparison.OrdinalIgnoreCase))
                {
                    usedByUnits.Add(new AbilityUnitUsage(unit.Id, unitName, "BaseClass"));
                }

                foreach (var passive in unit.Passives)
                {
                    if (ability.NameSid.Equals(passive.NameSid, StringComparison.OrdinalIgnoreCase))
                    {
                        usedByUnits.Add(new AbilityUnitUsage(unit.Id, unitName, "Passive"));
                        break;
                    }
                }

                foreach (var active in unit.Abilities)
                {
                    if (ability.NameSid.Equals(active.NameSid, StringComparison.OrdinalIgnoreCase))
                    {
                        var type = active.IsAlternativeAttack ? "Alternative" : "Active";
                        usedByUnits.Add(new AbilityUnitUsage(unit.Id, unitName, type));
                        break;
                    }
                }
            }
        }

        var iconPath = ability.NameSid != null
            ? $"icons/abilities/{AbilityIconHelper.ApplyTierIconOffset(ability.NameSid)}"
            : $"icons/abilities/{ability.AbilityId}";

        return new AbilityDetailsResult(
            AbilityId: ability.AbilityId,
            Name: name,
            Description: description,
            AbilityType: ability.AbilityType,
            IconPath: iconPath,
            AbilityTypeSid: ability.AbilityTypeSid,
            Rank: ability.Rank,
            EnergyCost: ability.EnergyCost,
            Immunities: immunities,
            InfoNotes: infoNotes,
            UsedByUnits: usedByUnits
        );
    }

    private static IEnumerable<string> EnumerateFallbacks(string sid)
    {
        yield return sid;
        if (sid.EndsWith("_alt", StringComparison.Ordinal)) yield return sid[..^4];
        if (sid.EndsWith("_alt2", StringComparison.Ordinal)) yield return sid[..^5];
        if (sid.EndsWith("_upg_alt", StringComparison.Ordinal)) yield return sid[..^8];
        if (sid.EndsWith("_upg", StringComparison.Ordinal)) yield return sid[..^4];
    }
}
