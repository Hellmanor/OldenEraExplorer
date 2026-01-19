namespace API.Contracts;

public record AbilityListItemDto(
    string Id,
    string Name,
    string AbilityType,
    string? Icon
);

/// Multiple units share identical abilities. Aggregation prevents duplication in ability list.
public record AbilityDetailDto(
    string Id,
    string Name,
    string? NameSid,
    string AbilityType,
    string? Description,
    int? Rank,
    int? EnergyCost,
    string? AbilityTypeSid,
    IReadOnlyList<string>? Immunities,
    IReadOnlyList<string>? InfoNotes,
    string? Icon,
    List<string>? SourceUnitIds = null,
    List<string>? SourceUnitNames = null,
    AbilityStatLabelsDto? StatLabels = null
);

public record AbilityStatLabelsDto(
    string CreaturesWithAbility
);
