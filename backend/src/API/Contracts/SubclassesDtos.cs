namespace API.Contracts;

public record SubclassListItemDto(
    string Id,
    string Name,
    string? Faction,
    string? FactionDisplay,
    string? ClassType,
    string? ClassDisplay,
    string? Icon
);

public record SubclassDetailDto(
    string Id,
    string Name,
    string? Description,
    string? Faction,
    string? FactionDisplay,
    string? FactionIcon,
    string? ClassType,
    string? ClassDisplay,
    string? ClassIcon,
    string? Icon,
    IReadOnlyList<RequiredSkillDto> RequiredSkills,
    SubclassStatLabelsDto? StatLabels = null
);

public record SubclassStatLabelsDto(
    string RequiredSkills
);

public record RequiredSkillDto(
    string SkillId,
    string SkillName,
    string? Icon
);
