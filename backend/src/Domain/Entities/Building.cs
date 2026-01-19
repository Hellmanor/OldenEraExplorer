using Domain.Contracts;

namespace Domain.Entities;

public sealed class Building : IEntity
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Faction { get; init; }
    public required int MaxLevel { get; init; }

    public required IReadOnlyList<string> Names { get; init; }
    public required IReadOnlyList<string> Descriptions { get; init; }

    public bool IsConstructedOnStart { get; init; }
    public int LevelOnStart { get; init; }
    public required string Icon { get; init; }

    public required IReadOnlyList<IReadOnlyList<string>> EffectsPerLevel { get; init; }
    public required IReadOnlyList<IReadOnlyList<BuildingCost>> CostsPerLevel { get; init; }

    public required IReadOnlyList<string> RecruitableUnits { get; init; }
}

public sealed record BuildingCost(string Resource, int Amount);
