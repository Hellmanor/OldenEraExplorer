namespace Localization.Resolution;

public sealed record ResolutionContext(string Locale)
{
    // Unit/Ability context
    public string? UnitId { get; init; }
    public int? AbilityIndex { get; init; }
    public bool? IsActiveAbility { get; init; }

    // Hero context
    public string? HeroSpecializationId { get; init; }
    public string? HeroAbilityId { get; init; }

    // Skill context
    public string? SkillId { get; init; }
    public string? SubSkillId { get; init; }
    public int? SkillLevel { get; init; }

    // Buff/Debuff context
    public string? BuffId { get; init; }
    public int? BuffStacks { get; init; }
    public int? BuffSpellPower { get; init; }

    // Item/Artifact context
    public string? ItemId { get; init; }
    public int? ItemLevel { get; init; }
    public string? ItemSetId { get; init; }

    // Magic/Spell context
    public string? MagicId { get; init; }
    public int? MagicLevel { get; init; }

    // Building/Law context
    public string? FractionId { get; init; }
    public string? LawId { get; init; }
    public int? LawLevel { get; init; }

    // Map object context
    public string? MapObjectId { get; init; }
}
