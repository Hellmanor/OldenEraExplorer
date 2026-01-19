#nullable enable
namespace AssetExtractor.Models;

public record ExtractionProgress(
    string Phase,
    int Current,
    int Total,
    string? CurrentItem = null
)
{
    public double Percentage => Total > 0 ? (double)Current / Total * 100 : 0;
    public bool IsComplete => Current >= Total;
}
public record ExtractionProgressInfo(
    string Phase,
    int Current,
    int Total,
    string? CurrentItem = null
) : ExtractionProgress(Phase, Current, Total, CurrentItem);
