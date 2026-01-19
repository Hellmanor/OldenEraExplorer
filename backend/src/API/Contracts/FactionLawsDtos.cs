namespace API.Contracts;

public record FactionLawListItemDto(
    string Id,
    string Name,
    string? Faction,
    string? FactionDisplay,
    string? Icon
);

public record FactionLawDetailDto(
    string Id,
    string Name,
    string? LocalizedName,
    string? Faction,
    string? FactionDisplay,
    string? FactionIcon,
    string? Icon,
    IReadOnlyList<FactionLawLevelDto>? Levels,
    FactionLawStatLabelsDto? StatLabels = null
);

public record FactionLawStatLabelsDto(
    string Cost
);

public record FactionLawLevelDto(
    int Level,
    int Cost,
    string? Description
);
