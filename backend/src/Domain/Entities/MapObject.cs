using Domain.Contracts;

namespace Domain.Entities;

public sealed class MapObject : IEntity
{
    public required string Id { get; init; }
    public required string Tag { get; init; }
    public bool IsInteractable { get; init; }

    public int SizeX { get; init; }
    public int SizeZ { get; init; }

    public required string PrefabPath { get; init; }

    public string? NameSid { get; init; }
    public string? DescriptionSid { get; init; }
    public string? NarrativeDescriptionSid { get; init; }
}
