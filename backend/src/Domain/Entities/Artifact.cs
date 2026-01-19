using Domain.Contracts;

namespace Domain.Entities;

public sealed class Artifact : IEntity
{
    public required string Id { get; init; }
    public required string NameSid { get; init; }
    public required string DescSid { get; init; }
    public required string Rarity { get; init; }
    public required string Slot { get; init; }
    public required string Icon { get; init; }
    public string? ItemSetId { get; init; }

    public string? NarrativeDescSid { get; init; }
    public string? UpgradeDescSid { get; init; }

    public required int MaxLevel { get; init; }
    public required int CostBase { get; init; }
    public required int CostPerLevel { get; init; }
}
