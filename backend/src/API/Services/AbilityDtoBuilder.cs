using API.Contracts;
using GameData.Indexing;
using Localization.Indexing;
using Localization.Resolution;
using GameData.Shared.Utils;

namespace API.Services;

public static class AbilityDtoBuilder
{
    public static AbilityDetailDto BuildFromAbilityRef(
        DbIndex.AbilityRef abilityRef,
        string unitId,
        int abilityIndex,
        ITextResolver resolver,
        LangIndex lang,
        string locale,
        string? variantId = null)
    {
        var abilityType = abilityRef.IsActiveAbility ? "Active" : "Passive";
        if (abilityRef.IsAlternativeAttack)
            abilityType = "Alternative";

        var abilityId = variantId ?? BuildAbilityId(abilityRef.NameSid, unitId, abilityIndex, abilityRef.IsActiveAbility);

        var name = ResolveAbilityName(resolver, lang, abilityRef.NameSid, locale) ?? abilityRef.NameSid ?? abilityId;

        var description = ResolveAbilityDescription(resolver, lang, abilityRef.DescriptionSid, unitId, abilityIndex, abilityRef.IsActiveAbility, locale);

        string? abilityTypeSid = null;
        if (!string.IsNullOrWhiteSpace(abilityRef.AbilityTypeSid))
        {
            abilityTypeSid = TryResolveText(resolver, abilityRef.AbilityTypeSid, locale) ?? abilityRef.AbilityTypeSid;
        }

        var immunities = new List<string>();
        foreach (var immSid in abilityRef.ImmunitySids)
        {
            var resolved = TryResolveText(resolver, immSid, locale);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                immunities.Add(resolved);
            }
            else if (!string.IsNullOrWhiteSpace(immSid))
            {
                immunities.Add(immSid);
            }
        }

        var infoNotes = new List<string>();
        foreach (var infoSid in abilityRef.InfoDescriptionSids)
        {
            var resolved = TryResolveText(resolver, infoSid, locale);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                infoNotes.Add(resolved);
            }
        }

        string? iconPath = null;
        if (!string.IsNullOrWhiteSpace(abilityRef.Icon))
        {
            // Apply tier icon offset for defence trio (evasive, resistant, steelskin)
            var iconName = AbilityIconHelper.ApplyTierIconOffset(abilityRef.Icon);
            iconPath = $"icons/abilities/{iconName}";
        }
        else if (!string.IsNullOrWhiteSpace(abilityRef.NameSid))
        {
            // Apply tier icon offset for defence trio (evasive, resistant, steelskin)
            var iconName = AbilityIconHelper.ApplyTierIconOffset(abilityRef.NameSid);
            iconPath = $"icons/abilities/{iconName}";
        }

        return new AbilityDetailDto(
            Id: abilityId,
            Name: name,
            NameSid: abilityRef.NameSid,
            AbilityType: abilityType,
            Description: description,
            Rank: abilityRef.Rank,
            EnergyCost: abilityRef.Energy,
            AbilityTypeSid: abilityTypeSid,
            Immunities: immunities.Count > 0 ? immunities : null,
            InfoNotes: infoNotes.Count > 0 ? infoNotes : null,
            Icon: iconPath,
            SourceUnitIds: null,
            SourceUnitNames: null,
            StatLabels: null
        );
    }

    private static string BuildAbilityId(string? nameSid, string unitId, int index, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(nameSid))
            return $"{unitId}_{(isActive ? "active" : "passive")}_{index}";

        return $"{nameSid}-{unitId}-{index}";
    }

    public static string? ResolveAbilityName(
        ITextResolver resolver,
        LangIndex lang,
        string? nameSid,
        string locale)
    {
        if (string.IsNullOrWhiteSpace(nameSid))
            return null;

        var result = TryResolveText(resolver, nameSid, locale);
        if (!string.IsNullOrWhiteSpace(result))
            return result;

        return lang.ResolveText(nameSid);
    }

    public static string? ResolveAbilityDescription(
        ITextResolver resolver,
        LangIndex lang,
        string? descriptionSid,
        string? unitId,
        int abilityIndex,
        bool isActiveAbility,
        string locale)
    {
        if (string.IsNullOrWhiteSpace(descriptionSid))
            return null;

        var ctx = (!string.IsNullOrWhiteSpace(unitId) && unitId != "(standalone)" && unitId != "(hero)")
            ? new ResolutionContext(locale) { UnitId = unitId, AbilityIndex = abilityIndex, IsActiveAbility = isActiveAbility }
            : new ResolutionContext(locale);

        try
        {
            var result = resolver.Resolve(descriptionSid, ctx, out _);
            if (!string.IsNullOrWhiteSpace(result) && result != descriptionSid)
                return result;
        }
        catch
        {
        }

        return lang.ResolveText(descriptionSid);
    }

    public static string? TryResolveText(ITextResolver resolver, string sid, string locale)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return null;

        try
        {
            var ctx = new ResolutionContext(locale);
            var result = resolver.Resolve(sid, ctx, out _);

            if (!string.IsNullOrWhiteSpace(result) && result != sid)
            {
                return result;
            }
        }
        catch
        {
        }

        return null;
    }
}
