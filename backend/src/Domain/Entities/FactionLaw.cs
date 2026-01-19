using Domain.Contracts;

namespace Domain.Entities;

public sealed class FactionLaw : IEntity
{
    public required string Id { get; init; }
    public required string NameSid { get; init; }
    public required string DescSid { get; init; }
    public required string Icon { get; init; }
    public required string Faction { get; init; }

    public required IReadOnlyList<LevelParameters> ParametersPerLevel { get; init; }
}

public sealed record LevelParameters(
    int Cost,
    IReadOnlyList<BonusEffect> Bonuses
);

public sealed record BonusEffect(
    string Type,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<string> Receivers,
    string Faction
);
