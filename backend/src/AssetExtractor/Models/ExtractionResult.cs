#nullable enable
namespace AssetExtractor.Models;

public class ExtractionResult
{
    public bool Success { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string PrefabName { get; set; } = string.Empty;
    public List<string> TexturePaths { get; set; } = new();
}

public class BatchExtractionResult
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalCount { get; set; }
    public List<string> FailedItems { get; set; } = new();
    public List<ExtractionResult> Results { get; set; } = new();
}
