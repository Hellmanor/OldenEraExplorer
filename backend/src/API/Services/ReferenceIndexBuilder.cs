using API.Models;
using GameData.Indexing;
using GameData.Loading;
using Localization.Indexing;

namespace API.Services;

public sealed class ReferenceIndexBuilder
{
    public async Task<ReferenceIndex> BuildAsync(
        IDataCatalog catalog,
        LangIndex lang,
        string? streamingAssetsRoot = null,
        CancellationToken ct = default)
    {
        var index = new ReferenceIndex();

        var unitsTask = catalog.GetUnitsAsync();
        var heroesTask = catalog.GetHeroesAsync();
        var spellsTask = catalog.GetSpellsAsync();
        var artifactsTask = catalog.GetArtifactsAsync();
        var buildingsTask = catalog.GetBuildingsAsync();

        await Task.WhenAll(unitsTask, heroesTask, spellsTask, artifactsTask, buildingsTask).ConfigureAwait(false);

        var units = await unitsTask.ConfigureAwait(false);
        var heroes = await heroesTask.ConfigureAwait(false);
        var spells = await spellsTask.ConfigureAwait(false);
        var artifacts = await artifactsTask.ConfigureAwait(false);
        var buildings = await buildingsTask.ConfigureAwait(false);

        HeroSpecializationsIndex? heroSpecializations = null;
        if (!string.IsNullOrWhiteSpace(streamingAssetsRoot))
        {
            heroSpecializations = new HeroSpecializationsIndex();
            heroSpecializations.Scan(streamingAssetsRoot);
        }

        BuildHeroReferences(index, heroes, heroSpecializations, lang);

        BuildUnitReferences(index, units, lang);

        BuildArtifactReferences(index, artifacts, lang);

        BuildBuildingReferences(index, buildings, lang);

        return index;
    }

    private void BuildHeroReferences(
        ReferenceIndex index,
        IReadOnlyList<HeroesIndex.HeroRecord> heroes,
        HeroSpecializationsIndex? heroSpecializations,
        LangIndex lang)
    {
        foreach (var hero in heroes)
        {
            if (hero.IsTutorialOrCampaignHero)
                continue;

            var heroName = lang.ResolveText(hero.HeroId)
                ?? lang.ResolveText($"{hero.HeroId}_name")
                ?? hero.HeroId;

            foreach (var squad in hero.StartSquad)
            {
                var unitName = lang.ResolveText($"{squad.Sid}_name") ?? squad.Sid;
                index.AddReference(
                    hero.HeroId, EntityType.Hero,
                    squad.Sid, EntityType.Unit,
                    "StartingArmy",
                    heroName, unitName);
            }

            foreach (var skill in hero.StartSkills)
            {
                var skillName = lang.ResolveText($"{skill.Sid}_name") ?? skill.Sid;
                index.AddReference(
                    hero.HeroId, EntityType.Hero,
                    skill.Sid, EntityType.Skill,
                    $"StartingSkills[Level {skill.Level}]",
                    heroName, skillName);
            }

            var effectiveSpells = hero.StartMagics.ToList();
            if (heroSpecializations != null && !string.IsNullOrWhiteSpace(hero.SpecializationSid))
            {
                effectiveSpells = heroSpecializations.ApplyReplacements(hero.SpecializationSid, effectiveSpells);
            }

            foreach (var spellId in effectiveSpells)
            {
                var spellName = lang.ResolveText($"{spellId}_name") ?? spellId;
                index.AddReference(
                    hero.HeroId, EntityType.Hero,
                    spellId, EntityType.Spell,
                    "StartingSpells",
                    heroName, spellName);
            }

            if (!string.IsNullOrEmpty(hero.SpecializationSid))
            {
                var subclassName = lang.ResolveText($"{hero.SpecializationSid}_name") ?? hero.SpecializationSid;
                index.AddReference(
                    hero.HeroId, EntityType.Hero,
                    hero.SpecializationSid, EntityType.Subclass,
                    "Specialization",
                    heroName, subclassName);
            }
        }
    }

    private void BuildUnitReferences(
        ReferenceIndex index,
        IReadOnlyList<DbIndex.UnitRecord> units,
        LangIndex lang)
    {
        foreach (var unit in units)
        {
            var unitName = lang.ResolveText($"{unit.Id}_name") ?? unit.Id;

            foreach (var ability in unit.Abilities)
            {
                var abilityName = lang.ResolveText(ability.NameSid) ?? ability.NameSid;
                index.AddReference(
                    unit.Id, EntityType.Unit,
                    ability.NameSid, EntityType.Ability,
                    "Abilities",
                    unitName, abilityName);
            }

            foreach (var passive in unit.Passives)
            {
                var passiveName = lang.ResolveText(passive.NameSid) ?? passive.NameSid;
                index.AddReference(
                    unit.Id, EntityType.Unit,
                    passive.NameSid, EntityType.Ability,
                    "Passives",
                    unitName, passiveName);
            }
        }
    }

    private void BuildArtifactReferences(
        ReferenceIndex index,
        IReadOnlyList<ArtifactsIndex.ArtifactRecord> artifacts,
        LangIndex lang)
    {
        var artifactsBySet = artifacts
            .Where(a => !string.IsNullOrEmpty(a.ItemSetId))
            .GroupBy(a => a.ItemSetId)
            .ToDictionary(g => g.Key!, g => g.ToList());

        foreach (var artifact in artifacts)
        {
            var artifactName = lang.ResolveText(artifact.NameSid) ?? artifact.Id;

            if (!string.IsNullOrEmpty(artifact.ItemSetId) &&
                artifactsBySet.TryGetValue(artifact.ItemSetId, out var setMembers))
            {
                var setName = lang.ResolveText($"{artifact.ItemSetId}_name") ?? artifact.ItemSetId;

                index.AddReference(
                    artifact.Id, EntityType.Artifact,
                    artifact.ItemSetId, EntityType.Artifact,
                    "SetMembership",
                    artifactName, setName);
            }
        }
    }

    private void BuildBuildingReferences(
        ReferenceIndex index,
        IReadOnlyList<BuildingsIndex.BuildingRecord> buildings,
        LangIndex lang)
    {
        foreach (var building in buildings)
        {
            var maxLevel = Math.Max(building.Names.Length, building.Descriptions.Length);
            if (maxLevel == 0) maxLevel = 1;

            for (int level = 0; level < maxLevel; level++)
            {
                var buildingId = $"{building.Faction}_{building.Sid}_L{level + 1}";

                var buildingName = level < building.Names.Length
                    ? (lang.ResolveText(building.Names[level]) ?? building.Sid)
                    : building.Sid;

                if (building.RecruitableUnitsPerLevel != null && building.RecruitableUnitsPerLevel.Length > 0)
                {
                    var unitsLevelIndex = Math.Min(level, building.RecruitableUnitsPerLevel.Length - 1);
                    var levelUnits = building.RecruitableUnitsPerLevel[unitsLevelIndex] ?? Array.Empty<string>();

                    foreach (var unitSid in levelUnits)
                    {
                        var unitName = lang.ResolveText($"{unitSid}_name") ?? unitSid;
                        index.AddReference(
                            buildingId, EntityType.Building,
                            unitSid, EntityType.Unit,
                            "RecruitableUnits",
                            buildingName, unitName);
                    }
                }

                if (level < building.RequiredBuildingsPerLevel.Length)
                {
                    var requirements = building.RequiredBuildingsPerLevel[level];
                    if (requirements != null)
                    {
                        foreach (var requirement in requirements)
                        {
                            var requiredBuildingId = $"{building.Faction}_{requirement.Sid}_L{requirement.Level}";

                            var requiredBuilding = buildings.FirstOrDefault(b =>
                                b.Faction == building.Faction && b.Sid == requirement.Sid);

                            string requiredBuildingName;
                            if (requiredBuilding != null && requirement.Level - 1 < requiredBuilding.Names.Length)
                            {
                                requiredBuildingName = lang.ResolveText(requiredBuilding.Names[requirement.Level - 1])
                                    ?? requirement.Sid;
                            }
                            else
                            {
                                requiredBuildingName = lang.ResolveText($"{requirement.Sid}_name")
                                    ?? requirement.Sid;
                            }

                            index.AddReference(
                                buildingId, EntityType.Building,
                                requiredBuildingId, EntityType.Building,
                                $"RequiredBuilding",
                                buildingName, requiredBuildingName);
                        }
                    }
                }
            }
        }
    }
}
