using Domain.Contracts;

namespace Domain.Entities;

public sealed class Spell : IEntity
{
    public required string Id { get; init; }
    public required string NameSid { get; init; }
    public required string DescSid { get; init; }
    public required string Icon { get; init; }
    public required int Rank { get; init; }
    public required string School { get; init; }

    public bool IsSpecialMagic { get; init; }
    public bool UsedOnMap { get; init; }
    public bool HasBattleMagic { get; init; }
    public bool HasWorldMagic { get; init; }

    public int DealersPerLevelsCount { get; init; }
    public int SettingPerLevelsCount { get; init; }
}
