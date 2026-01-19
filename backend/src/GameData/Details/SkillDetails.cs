using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GameData.Indexing;
using Localization.Indexing;
using Localization.Resolution;

namespace GameData.Details;

public sealed class SkillDetailsService
{
    private readonly SkillsIndex _skillsIndex;
    private readonly LangIndex _langIndex;
    private readonly ITextResolver? _textResolver;
    private readonly ILogger<SkillDetailsService> _logger;

    public SkillDetailsService(
        SkillsIndex skillsIndex,
        LangIndex langIndex,
        ITextResolver? textResolver,
        ILogger<SkillDetailsService> logger)
    {
        _skillsIndex = skillsIndex;
        _langIndex = langIndex;
        _textResolver = textResolver;
        _logger = logger;
    }

    public Task<SkillDetailsAssembler.SkillDetailsVM?> GetDetailsAsync(
        string skillId,
        string locale,
        CancellationToken cancellationToken = default)
    {
        if (!_skillsIndex.Skills.TryGetValue(skillId, out var skillRecord))
        {
            _logger.LogWarning("Skill not found: {SkillId}", skillId);
            return Task.FromResult<SkillDetailsAssembler.SkillDetailsVM?>(null);
        }

        var ctx = new ResolutionContext(locale);
        var assembler = new SkillDetailsAssembler();
        var details = assembler.Build(skillRecord, _skillsIndex, _langIndex, _textResolver, ctx);

        if (details == null)
        {
            _logger.LogWarning("Failed to build details for skill: {SkillId}", skillId);
            return Task.FromResult<SkillDetailsAssembler.SkillDetailsVM?>(null);
        }

        _logger.LogDebug(
            "Built details for skill {SkillId}: {SkillName} ({LevelCount} levels, {SubSkillCount} subskills)",
            skillId,
            details.SkillName,
            CountLevels(details),
            details.AllSubSkills.Count);

        return Task.FromResult<SkillDetailsAssembler.SkillDetailsVM?>(details);
    }

    private static int CountLevels(SkillDetailsAssembler.SkillDetailsVM details)
    {
        var count = 0;
        if (details.Level1 != null) count++;
        if (details.Level2 != null) count++;
        if (details.Level3 != null) count++;
        return count;
    }
}

public sealed class SkillDetailsAssembler
{
    public sealed class SkillDetailsVM
    {
        public string SkillId { get; init; } = "";
        public string SkillName { get; init; } = "";
        public string SkillType { get; init; } = "";
        public string SkillDescription { get; init; } = "";
        public string IconPlaceholder { get; init; } = "skill_placeholder";

        public SkillLevelVM? Level1 { get; init; }
        public SkillLevelVM? Level2 { get; init; }
        public SkillLevelVM? Level3 { get; init; }

        public IReadOnlyList<SubSkillCardVM> AllSubSkills { get; init; } = Array.Empty<SubSkillCardVM>();
    }

    public sealed class SkillLevelVM
    {
        public int Level { get; init; }
        public string LevelName { get; init; } = "";
        public string Description { get; init; } = "";
        public string Icon { get; init; } = "";
        public IReadOnlyList<SubSkillCardVM> SubSkillChoices { get; init; } = Array.Empty<SubSkillCardVM>();
    }

    public sealed class SubSkillCardVM
    {
        public string SubSkillId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Icon { get; init; } = "";
        public string IconPlaceholder { get; init; } = "subskill_placeholder";
    }

    public SkillDetailsVM? Build(
        SkillsIndex.SkillRecord skill,
        SkillsIndex skillsIndex,
        LangIndex lang,
        ITextResolver? resolver,
        ResolutionContext ctx)
    {
        if (skill is null || lang is null) return null;

        string ResolveVia(string sid, string? skillIdOverride = null, string? subSkillIdOverride = null, int? skillLevelOverride = null)
        {
            if (string.IsNullOrWhiteSpace(sid)) return "";
            if (resolver is not null)
            {
                var resolveCtx = ctx;
                if (skillIdOverride is not null || subSkillIdOverride is not null || skillLevelOverride is not null)
                {
                    resolveCtx = new ResolutionContext(ctx.Locale)
                    {
                        UnitId = ctx.UnitId,
                        AbilityIndex = ctx.AbilityIndex,
                        IsActiveAbility = ctx.IsActiveAbility,
                        HeroSpecializationId = ctx.HeroSpecializationId,
                        SkillId = skillIdOverride ?? ctx.SkillId,
                        SubSkillId = subSkillIdOverride ?? ctx.SubSkillId,
                        SkillLevel = skillLevelOverride ?? ctx.SkillLevel
                    };
                }

                var result = resolver.Resolve(sid, resolveCtx, out _);
                if (!string.IsNullOrWhiteSpace(result)) return result;
            }
            return lang.ResolveText(sid) ?? sid;
        }

        var skillName = ResolveVia(skill.NameSid, skillIdOverride: skill.SkillId);
        var skillDesc = ResolveVia(skill.DescSid, skillIdOverride: skill.SkillId);
        var skillType = skill.SkillType;

        SkillLevelVM? level1 = null, level2 = null, level3 = null;
        var allSubSkillCards = new List<SubSkillCardVM>();

        if (skill.LevelParams.Count >= 1)
        {
            var lvl1 = skill.LevelParams[0];
            level1 = new SkillLevelVM
            {
                Level = 1,
                LevelName = ResolveVia(lvl1.NameSid, skillIdOverride: skill.SkillId, skillLevelOverride: 1),
                Description = ResolveVia(lvl1.DescSid, skillIdOverride: skill.SkillId, skillLevelOverride: 1),
                Icon = lvl1.Icon,
                SubSkillChoices = Array.Empty<SubSkillCardVM>()
            };
        }

        if (skill.LevelParams.Count >= 2)
        {
            var lvl2 = skill.LevelParams[1];
            var subSkills2 = BuildSubSkillCards(lvl2.SubSkills, skill.SkillId, skillsIndex, lang, resolver, ctx);
            level2 = new SkillLevelVM
            {
                Level = 2,
                LevelName = ResolveVia(lvl2.NameSid, skillIdOverride: skill.SkillId, skillLevelOverride: 2),
                Description = ResolveVia(lvl2.DescSid, skillIdOverride: skill.SkillId, skillLevelOverride: 2),
                Icon = lvl2.Icon,
                SubSkillChoices = subSkills2
            };
            allSubSkillCards.AddRange(subSkills2);
        }

        if (skill.LevelParams.Count >= 3)
        {
            var lvl3 = skill.LevelParams[2];
            var subSkills3 = BuildSubSkillCards(lvl3.SubSkills, skill.SkillId, skillsIndex, lang, resolver, ctx);
            level3 = new SkillLevelVM
            {
                Level = 3,
                LevelName = ResolveVia(lvl3.NameSid, skillIdOverride: skill.SkillId, skillLevelOverride: 3),
                Description = ResolveVia(lvl3.DescSid, skillIdOverride: skill.SkillId, skillLevelOverride: 3),
                Icon = lvl3.Icon,
                SubSkillChoices = subSkills3
            };
            allSubSkillCards.AddRange(subSkills3);
        }

        return new SkillDetailsVM
        {
            SkillId = skill.SkillId,
            SkillName = skillName,
            SkillType = skillType,
            SkillDescription = skillDesc,
            IconPlaceholder = skill.LevelParams.Count > 0 ? skill.LevelParams[0].Icon : "skill_placeholder",
            Level1 = level1,
            Level2 = level2,
            Level3 = level3,
            AllSubSkills = allSubSkillCards
        };
    }

    private List<SubSkillCardVM> BuildSubSkillCards(
        List<string> subSkillIds,
        string skillId,
        SkillsIndex skillsIndex,
        LangIndex lang,
        ITextResolver? resolver,
        ResolutionContext ctx)
    {
        var cards = new List<SubSkillCardVM>();

        foreach (var subSkillId in subSkillIds)
        {
            if (!skillsIndex.SubSkills.TryGetValue(subSkillId, out var subSkill))
                continue;

            string ResolveVia(string sid, string? subSkillIdOverride = null)
            {
                if (string.IsNullOrWhiteSpace(sid)) return "";
                if (resolver is not null)
                {
                    var resolveCtx = new ResolutionContext(ctx.Locale)
                    {
                        UnitId = ctx.UnitId,
                        AbilityIndex = ctx.AbilityIndex,
                        IsActiveAbility = ctx.IsActiveAbility,
                        HeroSpecializationId = ctx.HeroSpecializationId,
                        SkillId = skillId,
                        SubSkillId = subSkillIdOverride ?? ctx.SubSkillId,
                        SkillLevel = ctx.SkillLevel
                    };

                    var result = resolver.Resolve(sid, resolveCtx, out _);
                    if (!string.IsNullOrWhiteSpace(result)) return result;
                }
                return lang.ResolveText(sid) ?? sid;
            }

            var name = ResolveVia(subSkill.NameSid, subSkillIdOverride: subSkillId);
            var desc = ResolveVia(subSkill.DescSid, subSkillIdOverride: subSkillId);

            cards.Add(new SubSkillCardVM
            {
                SubSkillId = subSkillId,
                Name = name,
                Description = desc,
                Icon = subSkill.Icon ?? "",
                IconPlaceholder = "subskill_placeholder"
            });
        }

        return cards;
    }
}
