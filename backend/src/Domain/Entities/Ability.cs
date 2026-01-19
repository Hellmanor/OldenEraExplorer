using Domain.Contracts;

namespace Domain.Entities;

public sealed class Ability : IEntity
{
    public required string Id { get; init; }
    public required string AbilityType { get; init; }

    public string? NameSid { get; init; }
    public string? DescriptionSid { get; init; }
    public string? AbilityTypeSid { get; init; }

    public required IReadOnlyList<string> ImmunitySids { get; init; }
    public required IReadOnlyList<string> InfoDescriptionSids { get; init; }

    public required string SourceUnitId { get; init; }

    public int? Rank { get; init; }
    public int? EnergyCost { get; init; }
}
