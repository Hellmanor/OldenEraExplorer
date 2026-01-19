using Domain.Contracts;

namespace Domain.Entities;

public sealed class Skill : IEntity
{
    public required string Id { get; init; }
    public required string NameSid { get; init; }
    public required string DescSid { get; init; }
    public required string SkillType { get; init; }
    public required int MaxLevel { get; init; }

    public required IReadOnlyList<string> AllSubSkills { get; init; }
    public required IReadOnlyList<SkillLevelParam> LevelParams { get; init; }

    /// Game uses pseudo-skills for internal mechanics. Hiding them prevents UI clutter from implementation details.
    public bool IsPseudoSkill { get; init; }
}

public sealed record SkillLevelParam(
    int Level,
    string NameSid,
    string DescSid
);

public sealed class SubSkill : IEntity
{
    public required string Id { get; init; }
    public required string NameSid { get; init; }
    public required string DescSid { get; init; }
}
