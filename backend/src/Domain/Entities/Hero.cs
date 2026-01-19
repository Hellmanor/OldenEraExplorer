using Domain.Contracts;

namespace Domain.Entities;

public sealed class Hero : IEntity
{
    public required string Id { get; init; }
    public required string Faction { get; init; }
    public required string ClassType { get; init; }
    public string? SpecializationSid { get; init; }

    public required string Mesh { get; init; }
    public required string Icon { get; init; }
    public required string ClassIcon { get; init; }
    public required string SpecializationIcon { get; init; }

    public required int CostGold { get; init; }
    public required int StartLevel { get; init; }
    public required IReadOnlyDictionary<string, int> BaseStats { get; init; }

    public required IReadOnlyList<SkillWithLevel> StartSkills { get; init; }
    public required IReadOnlyList<string> StartMagics { get; init; }
    public required IReadOnlyList<StartSquadUnit> StartSquad { get; init; }
}

public sealed record SkillWithLevel(string Sid, int Level);
public sealed record StartSquadUnit(string Sid, int Min, int Max);
