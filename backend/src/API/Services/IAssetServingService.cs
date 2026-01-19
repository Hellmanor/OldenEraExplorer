namespace API.Services;

public interface IAssetServingService
{
    string? ResolveIconPath(string relativePath, string? version = null);

    bool IconExists(string relativePath);

    IReadOnlyList<string> GetExtractedIcons();

    string ExtractedAssetsDirectory { get; }

    string? CurrentVersion { get; }

    /// <summary>
    /// Called when the game path changes to ensure assets are resolved from the correct version.
    /// </summary>
    void SetCurrentVersion(string? version);

    /// <summary>
    /// Cache invalidation: force a refresh on next GetExtractedIcons() call.
    /// </summary>
    void InvalidateCache();
}
