using Microsoft.Extensions.Logging;
using GameData.Indexing;

namespace GameData.Details;

public record UsedByHeroEntry(string HeroId, string HeroName);

public class UnitNavigationService
{
    private readonly ILogger<UnitNavigationService>? _logger;

    public UnitNavigationService(ILogger<UnitNavigationService>? logger = null)
    {
        _logger = logger;
    }

    public DbIndex.UnitRecord? GetPreviousUnit(
        DbIndex.UnitRecord currentUnit,
        IReadOnlyList<DbIndex.UnitRecord> allUnits)
    {
        if (currentUnit == null)
        {
            _logger?.LogWarning("GetPreviousUnit called with null currentUnit");
            return null;
        }

        if (allUnits == null || allUnits.Count == 0)
        {
            _logger?.LogWarning("GetPreviousUnit called with null or empty allUnits");
            return null;
        }

        var currentIndex = -1;
        for (int i = 0; i < allUnits.Count; i++)
        {
            if (allUnits[i].Id == currentUnit.Id)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex <= 0)
        {
            return null;
        }

        return allUnits[currentIndex - 1];
    }

    public DbIndex.UnitRecord? GetNextUnit(
        DbIndex.UnitRecord currentUnit,
        IReadOnlyList<DbIndex.UnitRecord> allUnits)
    {
        if (currentUnit == null)
        {
            _logger?.LogWarning("GetNextUnit called with null currentUnit");
            return null;
        }

        if (allUnits == null || allUnits.Count == 0)
        {
            _logger?.LogWarning("GetNextUnit called with null or empty allUnits");
            return null;
        }

        var currentIndex = -1;
        for (int i = 0; i < allUnits.Count; i++)
        {
            if (allUnits[i].Id == currentUnit.Id)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0 || currentIndex >= allUnits.Count - 1)
        {
            return null;
        }

        return allUnits[currentIndex + 1];
    }

    public (int Current, int Total) GetPosition(
        DbIndex.UnitRecord unit,
        IReadOnlyList<DbIndex.UnitRecord> allUnits)
    {
        if (unit == null || allUnits == null || allUnits.Count == 0)
            return (0, 0);

        for (int i = 0; i < allUnits.Count; i++)
        {
            if (allUnits[i].Id == unit.Id)
                return (i + 1, allUnits.Count);
        }

        return (0, allUnits.Count);
    }
}
