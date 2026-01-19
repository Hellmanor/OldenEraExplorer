using Domain.Contracts;

namespace Domain.Entities;

public sealed class Unit : IEntity
{
    public required string Id { get; init; }
    public required string Faction { get; init; }
    public required int Tier { get; init; }

    public string? MeshPath { get; init; }
    public float? Scale { get; init; }

    public string? BaseClassNameSid { get; init; }
    public string? BaseClassDescSid { get; init; }
    public string? BaseClassIcon { get; init; }

    public required IReadOnlyDictionary<string, string> Stats { get; init; }
    public int? Growth { get; init; }
    public required IReadOnlyList<UnitCostEntry> Cost { get; init; }

    public required IReadOnlyList<AbilityRef> Abilities { get; init; }
    public required IReadOnlyList<AbilityRef> Passives { get; init; }
}

public sealed record AbilityRef(
    string NameSid,
    string DescriptionSid,
    string Icon,
    string AbilityTypeSid,
    bool IsActiveAbility,
    bool IsAlternativeAttack,
    int? Rank,
    int? Energy,
    IReadOnlyList<string> ImmunitySids,
    IReadOnlyList<string> InfoDescriptionSids
);

public sealed record UnitCostEntry(string ResourceKey, int Amount);
