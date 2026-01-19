using System;
using System.Collections.Generic;
using GameData.Indexing;
using Localization.Indexing;

namespace GameData.Details;

public class SubclassDetailsService
{
    public SubclassDetailsResult GetDetails(
        SubclassesIndex.SubclassRecord subclass,
        string classDisplay,
        string subclassName,
        string description,
        LangIndex lang,
        SkillsIndex? skillsIndex)
    {
        if (subclass == null) throw new ArgumentNullException(nameof(subclass));
        if (lang == null) throw new ArgumentNullException(nameof(lang));

        var requiredSkills = ResolveRequiredSkills(subclass.RequiredSkills, lang, skillsIndex);

        return new SubclassDetailsResult(
            ClassName: classDisplay,
            SubclassName: subclassName,
            Description: description,
            Icon: subclass.Icon,
            RequiredSkills: requiredSkills
        );
    }

    private List<RequiredSkillEntry> ResolveRequiredSkills(
        List<SubclassesIndex.ActivationCondition> activationConditions,
        LangIndex lang,
        SkillsIndex? skillsIndex)
    {
        var requiredSkills = new List<RequiredSkillEntry>();

        if (activationConditions == null || activationConditions.Count == 0)
        {
            return requiredSkills;
        }

        foreach (var condition in activationConditions)
        {
            var skillName = condition.SkillSid;
            var skillIcon = "";

            if (skillsIndex?.Skills.TryGetValue(condition.SkillSid, out var skillRecord) == true)
            {
                const int expertLevelIndex = 2;

                if (skillRecord.LevelParams.Count > expertLevelIndex)
                {
                    var expertLevel = skillRecord.LevelParams[expertLevelIndex];
                    skillIcon = expertLevel.Icon ?? "";
                    skillName = lang.ResolveText(expertLevel.NameSid) ?? skillName;
                }
                else if (skillRecord.LevelParams.Count > 0)
                {
                    skillIcon = skillRecord.LevelParams[0].Icon ?? "";
                    skillName = lang.ResolveText(skillRecord.LevelParams[0].NameSid) ?? skillName;
                }
            }

            requiredSkills.Add(new RequiredSkillEntry(
                SkillId: condition.SkillSid,
                SkillName: skillName,
                SkillLevel: condition.SkillLevel,
                Icon: skillIcon
            ));
        }

        return requiredSkills;
    }
}

public record SubclassDetailsResult(
    string ClassName,
    string SubclassName,
    string Description,
    string Icon,
    List<RequiredSkillEntry> RequiredSkills
);

public record RequiredSkillEntry(
    string SkillId,
    string SkillName,
    int SkillLevel,
    string Icon
);
