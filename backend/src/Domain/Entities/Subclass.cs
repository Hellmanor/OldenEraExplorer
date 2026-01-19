using Domain.Contracts;

namespace Domain.Entities;

public sealed class Subclass : IEntity
{
    public required string Id { get; init; }
    public required string NameSid { get; init; }
    public required string DescSid { get; init; }
    public required string Icon { get; init; }
    public required string Faction { get; init; }
    public required string ClassType { get; init; }

    public required IReadOnlyList<ActivationCondition> RequiredSkills { get; init; }
}

public sealed record ActivationCondition(
    string SkillSid,
    int SkillLevel,
    IReadOnlyList<string> SubSkillSids
);
