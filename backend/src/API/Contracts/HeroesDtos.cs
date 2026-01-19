namespace API.Contracts;

public record HeroListItemDto(
    string Id,
    string Name,
    string? Faction,
    string? FactionDisplay,
    string? ClassType,
    string? ClassDisplay,
    string? IconPath
);

/// Game data includes Luck/Morale for heroes, but they're always zero. Omitted to avoid confusion.
public record HeroDetailDto(
    string Id,
    string Name,
    string? Faction,
    string? FactionDisplay,
    string? ClassType,
    string? ClassDisplay,
    string? IconPath,
    string? ClassIcon,
    string? SpecializationIcon,
    string? FactionIcon,
    string? Attack,
    string? Defence,
    string? SpellPower,
    string? Knowledge,
    string? SpecializationName,
    string? SpecializationDescription,
    IReadOnlyList<StartingArmyDto>? StartingArmy,
    IReadOnlyList<StartingSkillDto>? StartingSkills,
    IReadOnlyList<StartingSpellDto>? StartingSpells,
    string? Description,
    string? Motto,
    HeroStatLabelsDto? StatLabels
);

public record StartingArmyDto(
    string UnitId,
    string UnitName,
    string CountInterval,
    string? Icon
);

public record StartingSkillDto(
    string SkillId,
    string SkillName,
    string? Icon
);

public record StartingSpellDto(
    string SpellId,
    string SpellName,
    string? Icon,
    bool IsMasterful
);

public record HeroStatLabelsDto(
    string StartingArmy,
    string StartingSkills,
    string StartingSpells,
    string Biography,
    string Motto
);
