using API.Models;
using GameData.Indexing;
using Localization.Resolution;

namespace API.Services;

public interface IReferenceIndexService
{
    void BuildIndex(GameDataLoadResult data, string locale);

    IReadOnlyList<EntityRef> GetReferencedBy(string entityId, EntityType entityType);

    void Clear();
}

public class ReferenceIndexService : IReferenceIndexService
{
    private readonly ReferenceIndex _index = new();
    private readonly ILogger<ReferenceIndexService> _logger;

    public ReferenceIndexService(ILogger<ReferenceIndexService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void BuildIndex(GameDataLoadResult data, string locale)
    {
        _logger.LogInformation("Building reference index...");
        _index.Clear();

        var resolver = data.ResolverFacade;

        BuildUnitToSpellReferences(data, resolver, locale);

        BuildUnitToAbilityReferences(data, resolver, locale);

        BuildHeroToSkillReferences(data, resolver, locale);

        BuildHeroToUnitReferences(data, resolver, locale);

        BuildHeroToSpellReferences(data, resolver, locale);

        var (totalRefs, totalEntities) = _index.GetStats();
        _logger.LogInformation("Reference index built: {TotalRefs} references across {TotalEntities} entities",
            totalRefs, totalEntities);
    }

    private void BuildUnitToSpellReferences(GameDataLoadResult data, ITextResolver resolver, string locale)
    {
        foreach (var unit in data.Units)
        {
            var unitName = GetLocalizedUnitName(resolver, unit.Id, locale) ?? unit.Id;

            foreach (var ability in unit.Abilities.Concat(unit.Passives))
            {
                if (ability.NameSid.Contains("magic", StringComparison.OrdinalIgnoreCase))
                {
                    var spellId = TryExtractSpellIdFromSid(ability.NameSid);
                    if (spellId != null && data.SpellsIndex.Spells.ContainsKey(spellId))
                    {
                        var spell = data.SpellsIndex.Spells[spellId];
                        var spellName = TryResolveText(resolver, spell.NameSid, locale) ?? spell.Id;

                        _index.AddReference(
                            sourceId: unit.Id,
                            sourceType: EntityType.Unit,
                            targetId: spell.Id,
                            targetType: EntityType.Spell,
                            propertyPath: "Abilities",
                            sourceDisplayName: unitName,
                            targetDisplayName: spellName
                        );
                    }
                }
            }
        }
    }

    private void BuildUnitToAbilityReferences(GameDataLoadResult data, ITextResolver resolver, string locale)
    {
        foreach (var unit in data.Units)
        {
            var unitName = GetLocalizedUnitName(resolver, unit.Id, locale) ?? unit.Id;

            foreach (var ability in unit.Abilities.Concat(unit.Passives))
            {
                var abilityId = TryExtractAbilityId(ability.NameSid);
                if (abilityId != null && data.AbilityIndex.Abilities.ContainsKey(abilityId))
                {
                    var abilityRecord = data.AbilityIndex.Abilities[abilityId];
                    var abilityName = TryResolveText(resolver, abilityRecord.NameSid, locale) ?? abilityId;

                    _index.AddReference(
                        sourceId: unit.Id,
                        sourceType: EntityType.Unit,
                        targetId: abilityId,
                        targetType: EntityType.Ability,
                        propertyPath: "Abilities",
                        sourceDisplayName: unitName,
                        targetDisplayName: abilityName
                    );
                }
            }
        }
    }

    private void BuildHeroToSkillReferences(GameDataLoadResult data, ITextResolver resolver, string locale)
    {
        foreach (var hero in data.HeroesIndex.Heroes.Values)
        {
            var heroName = GetLocalizedHeroName(resolver, hero.HeroId, locale) ?? hero.HeroId;

            foreach (var skillEntry in hero.StartSkills)
            {
                if (data.SkillsIndex.Skills.ContainsKey(skillEntry.Sid))
                {
                    var skill = data.SkillsIndex.Skills[skillEntry.Sid];
                    var skillName = TryResolveText(resolver, skill.NameSid, locale) ?? skill.SkillId;

                    _index.AddReference(
                        sourceId: hero.HeroId,
                        sourceType: EntityType.Hero,
                        targetId: skillEntry.Sid,
                        targetType: EntityType.Skill,
                        propertyPath: "StartingSkills",
                        sourceDisplayName: heroName,
                        targetDisplayName: skillName
                    );
                }
            }
        }
    }

    private void BuildHeroToUnitReferences(GameDataLoadResult data, ITextResolver resolver, string locale)
    {
        foreach (var hero in data.HeroesIndex.Heroes.Values)
        {
            var heroName = GetLocalizedHeroName(resolver, hero.HeroId, locale) ?? hero.HeroId;

            foreach (var unitEntry in hero.StartSquad)
            {
                var unit = data.Units.FirstOrDefault(u =>
                    u.Id.Equals(unitEntry.Sid, StringComparison.OrdinalIgnoreCase));

                if (unit != null)
                {
                    var unitName = GetLocalizedUnitName(resolver, unit.Id, locale) ?? unit.Id;

                    _index.AddReference(
                        sourceId: hero.HeroId,
                        sourceType: EntityType.Hero,
                        targetId: unit.Id,
                        targetType: EntityType.Unit,
                        propertyPath: "StartingArmy",
                        sourceDisplayName: heroName,
                        targetDisplayName: unitName
                    );
                }
            }
        }
    }

    /// <summary>
    /// Hero specializations can replace spells via heroMagicReplace bonuses.
    /// For example, a hero might have "haste" in StartMagics, but their specialization
    /// replaces it with "haste_spec" (Masterful Haste). We must use ApplyReplacements
    /// to get the effective spell list.
    /// </summary>
    private void BuildHeroToSpellReferences(GameDataLoadResult data, ITextResolver resolver, string locale)
    {
        var heroSpecializations = data.HeroSpecializationsIndex;

        foreach (var hero in data.HeroesIndex.Heroes.Values)
        {
            if (hero.IsTutorialOrCampaignHero)
                continue;

            var heroName = GetLocalizedHeroName(resolver, hero.HeroId, locale) ?? hero.HeroId;

            var effectiveSpells = hero.StartMagics.ToList();
            if (heroSpecializations != null && !string.IsNullOrWhiteSpace(hero.SpecializationSid))
            {
                effectiveSpells = heroSpecializations.ApplyReplacements(hero.SpecializationSid, effectiveSpells);
            }

            foreach (var spellId in effectiveSpells)
            {
                if (data.SpellsIndex.Spells.TryGetValue(spellId, out var spell))
                {
                    var spellName = TryResolveText(resolver, spell.NameSid, locale) ?? spell.Id;

                    _index.AddReference(
                        sourceId: hero.HeroId,
                        sourceType: EntityType.Hero,
                        targetId: spell.Id,
                        targetType: EntityType.Spell,
                        propertyPath: "StartingSpell",
                        sourceDisplayName: heroName,
                        targetDisplayName: spellName
                    );
                }
            }
        }
    }

    public IReadOnlyList<EntityRef> GetReferencedBy(string entityId, EntityType entityType)
    {
        return _index.GetReferencedBy(entityId, entityType);
    }

    public void Clear()
    {
        _index.Clear();
    }

    private string? TryExtractSpellIdFromSid(string sid)
    {
        var parts = sid.Split('_');
        var magicIndex = Array.IndexOf(parts, "magic");
        if (magicIndex >= 0 && magicIndex < parts.Length - 1)
        {
            return string.Join("_", parts.Skip(magicIndex + 1));
        }
        return null;
    }

    private string? TryExtractAbilityId(string sid)
    {
        var parts = sid.Split('_');
        if (parts.Length > 1)
        {
            return string.Join("_", parts);
        }
        return sid;
    }

    private string? GetLocalizedUnitName(ITextResolver resolver, string unitId, string locale)
    {
        var patterns = new[]
        {
            $"unit.{unitId}.name",
            $"unit_{unitId}_name",
            $"{unitId}_name",
            $"units.{unitId}.name"
        };

        foreach (var pattern in patterns)
        {
            var result = TryResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result) && !result.StartsWith("{") && result != pattern)
            {
                return result;
            }
        }

        return null;
    }

    private string? GetLocalizedHeroName(ITextResolver resolver, string heroId, string locale)
    {
        var patterns = new[]
        {
            heroId,
            $"{heroId}_name"
        };

        foreach (var pattern in patterns)
        {
            var result = TryResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result) && !result.StartsWith("{") && result != pattern)
            {
                return result;
            }
        }

        return null;
    }

    private string? TryResolveText(ITextResolver resolver, string? sid, string locale)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return null;
        }

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
