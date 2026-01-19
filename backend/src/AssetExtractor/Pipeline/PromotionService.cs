#nullable enable
using AssetExtractor.Models.Manifest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Pipeline;

/// <summary>
/// Handles asset promotion across multiple game versions.
/// Analyzes assets to determine shared/partial/build-specific status
/// and optimizes storage by moving shared assets to a common folder.
/// </summary>
public class PromotionService
{
    private readonly ILogger<PromotionService> _logger;
    private readonly ManifestManager _manifestManager;
    private readonly string _outputPath;

    public PromotionService(
        ManifestManager manifestManager,
        string outputPath,
        ILogger<PromotionService>? logger = null)
    {
        _logger = logger ?? NullLogger<PromotionService>.Instance;
        _manifestManager = manifestManager;
        _outputPath = outputPath;
    }

    public class PromotionResult
    {
        public int TotalAssets { get; set; }
        public int SharedAssets { get; set; }
        public int PartialAssets { get; set; }
        public int BuildSpecificAssets { get; set; }
        public long BytesSaved { get; set; }
        public int FilesDeleted { get; set; }
        public int FilesCopied { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Manifest keys are disambiguated due to collisions (e.g., same name for .png and .glb).
    /// </summary>
    private static string BuildFilePath(string basePath, string relativePath, string extension)
    {
        var fileName = relativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? relativePath
            : relativePath + extension;
        return Path.Combine(basePath, fileName);
    }

    public async Task<PromotionResult> PromoteAssetsAsync()
    {
        var result = new PromotionResult();
        var manifest = _manifestManager.Manifest;

        if (manifest.Builds.Count < 2)
        {
            _logger.LogInformation("Promotion requires at least 2 versions. Skipping");
            return result;
        }

        _logger.LogInformation(
            "Starting asset promotion across {BuildCount} versions",
            manifest.Builds.Count);

        var allVersions = new HashSet<string>(manifest.Builds.Keys);
        var sharedDir = _manifestManager.GetSharedOutputPath();

        Directory.CreateDirectory(sharedDir);

        var assets = _manifestManager.GetAllAssets();
        result.TotalAssets = assets.Count;

        var updatedAssets = new Dictionary<string, AssetInfoV3>();

        foreach (var (relativePath, assetInfo) in assets)
        {
            try
            {
                var promotedInfo = await PromoteSingleAssetAsync(
                    relativePath,
                    assetInfo,
                    allVersions,
                    sharedDir,
                    result);

                updatedAssets[relativePath] = promotedInfo;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error promoting {relativePath}: {ex.Message}");
                _logger.LogError(ex, "Failed to promote asset {RelativePath}", relativePath);

                updatedAssets[relativePath] = assetInfo;
            }
        }

        _manifestManager.ReplaceAllAssets(updatedAssets);
        _manifestManager.SetLastPromotionRun(DateTime.UtcNow);
        _manifestManager.SaveManifest();

        _logger.LogInformation("Promotion complete");
        _logger.LogInformation("  Total assets: {TotalAssets}", result.TotalAssets);
        _logger.LogInformation("  Shared: {SharedAssets}", result.SharedAssets);
        _logger.LogInformation("  Partial: {PartialAssets}", result.PartialAssets);
        _logger.LogInformation("  Build-specific: {BuildSpecificAssets}", result.BuildSpecificAssets);
        _logger.LogInformation("  Files copied to shared: {FilesCopied}", result.FilesCopied);
        _logger.LogInformation("  Duplicate files deleted: {FilesDeleted}", result.FilesDeleted);
        _logger.LogInformation("  Bytes saved: {BytesSaved:N0}", result.BytesSaved);

        if (result.Errors.Count > 0)
        {
            _logger.LogWarning("  Errors encountered: {ErrorCount}", result.Errors.Count);
        }

        return result;
    }

    private async Task<AssetInfoV3> PromoteSingleAssetAsync(
        string relativePath,
        AssetInfoV3 assetInfo,
        HashSet<string> allVersions,
        string sharedDir,
        PromotionResult result)
    {
        if (assetInfo.Variants == null || assetInfo.Variants.Count == 0)
        {
            result.BuildSpecificAssets++;
            return assetInfo;
        }

        var hashToVersions = new Dictionary<string, List<string>>();

        foreach (var (version, variant) in assetInfo.Variants)
        {
            if (!hashToVersions.TryGetValue(variant.Hash, out var versions))
            {
                versions = new List<string>();
                hashToVersions[variant.Hash] = versions;
            }
            versions.Add(version);
        }

        var mostCommon = hashToVersions
            .OrderByDescending(kvp => kvp.Value.Count)
            .First();

        var mostCommonHash = mostCommon.Key;
        var mostCommonVersions = mostCommon.Value;
        var mostCommonCount = mostCommonVersions.Count;
        var totalVersionCount = allVersions.Count;

        if (mostCommonCount == totalVersionCount && hashToVersions.Count == 1)
        {
            return await PromoteToSharedAsync(
                relativePath, assetInfo, mostCommonHash, mostCommonVersions, sharedDir, result);
        }
        else if (mostCommonCount >= 2)
        {
            return await PromoteToPartialAsync(
                relativePath, assetInfo, mostCommonHash, mostCommonVersions, hashToVersions, sharedDir, result);
        }
        else
        {
            result.BuildSpecificAssets++;
            assetInfo.Status = "build-specific";
            assetInfo.SharedHash = null;
            assetInfo.Size = null;
            assetInfo.Versions = null;
            return assetInfo;
        }
    }

    /// <summary>
    /// Identical in ALL versions.
    /// </summary>
    private async Task<AssetInfoV3> PromoteToSharedAsync(
        string relativePath,
        AssetInfoV3 assetInfo,
        string hash,
        List<string> versions,
        string sharedDir,
        PromotionResult result)
    {
        result.SharedAssets++;

        long size = assetInfo.Variants!.Values.First().Size;

        string? sourceFile = null;
        string? sourceVersion = null;

        foreach (var version in versions)
        {
            var versionPath = _manifestManager.GetVersionOutputPath(version);
            var filePath = BuildFilePath(versionPath, relativePath, assetInfo.Extension);

            if (File.Exists(filePath))
            {
                sourceFile = filePath;
                sourceVersion = version;
                break;
            }
        }

        if (sourceFile == null)
        {
            _logger.LogWarning("No source file found for shared asset: {RelativePath}", relativePath);
            return assetInfo;
        }

        var sharedPath = BuildFilePath(sharedDir, relativePath, assetInfo.Extension);
        var sharedPathDir = Path.GetDirectoryName(sharedPath);

        if (!string.IsNullOrEmpty(sharedPathDir) && !Directory.Exists(sharedPathDir))
        {
            Directory.CreateDirectory(sharedPathDir);
        }

        if (!File.Exists(sharedPath))
        {
            await Task.Run(() => File.Copy(sourceFile, sharedPath, overwrite: false));
            result.FilesCopied++;
            _logger.LogInformation("Copied to shared: {RelativePath}", relativePath);
        }

        foreach (var version in versions)
        {
            var versionPath = _manifestManager.GetVersionOutputPath(version);
            var filePath = BuildFilePath(versionPath, relativePath, assetInfo.Extension);

            if (File.Exists(filePath))
            {
                await Task.Run(() => File.Delete(filePath));
                result.FilesDeleted++;
                result.BytesSaved += size;
            }
        }

        return new AssetInfoV3
        {
            Type = assetInfo.Type,
            Status = "shared",
            SharedHash = hash,
            Size = size,
            Versions = new List<string>(versions),
            Extension = assetInfo.Extension,
            Variants = null
        };
    }

    /// <summary>
    /// Identical in 2+ versions but not all.
    /// </summary>
    private async Task<AssetInfoV3> PromoteToPartialAsync(
        string relativePath,
        AssetInfoV3 assetInfo,
        string sharedHash,
        List<string> sharedVersions,
        Dictionary<string, List<string>> hashToVersions,
        string sharedDir,
        PromotionResult result)
    {
        result.PartialAssets++;

        long sharedSize = 0;
        foreach (var version in sharedVersions)
        {
            if (assetInfo.Variants!.TryGetValue(version, out var variant))
            {
                sharedSize = variant.Size;
                break;
            }
        }

        string? sourceFile = null;

        foreach (var version in sharedVersions)
        {
            var versionPath = _manifestManager.GetVersionOutputPath(version);
            var filePath = BuildFilePath(versionPath, relativePath, assetInfo.Extension);

            if (File.Exists(filePath))
            {
                sourceFile = filePath;
                break;
            }
        }

        if (sourceFile != null)
        {
            var sharedPath = BuildFilePath(sharedDir, relativePath, assetInfo.Extension);
            var sharedPathDir = Path.GetDirectoryName(sharedPath);

            if (!string.IsNullOrEmpty(sharedPathDir) && !Directory.Exists(sharedPathDir))
            {
                Directory.CreateDirectory(sharedPathDir);
            }

            if (!File.Exists(sharedPath))
            {
                await Task.Run(() => File.Copy(sourceFile, sharedPath, overwrite: false));
                result.FilesCopied++;
                _logger.LogInformation("Copied to shared (partial): {RelativePath}", relativePath);
            }

            foreach (var version in sharedVersions)
            {
                var versionPath = _manifestManager.GetVersionOutputPath(version);
                var filePath = BuildFilePath(versionPath, relativePath, assetInfo.Extension);

                if (File.Exists(filePath))
                {
                    await Task.Run(() => File.Delete(filePath));
                    result.FilesDeleted++;
                    result.BytesSaved += sharedSize;
                }
            }
        }

        var newVariants = new Dictionary<string, AssetVariantV3>();

        foreach (var (hash, versions) in hashToVersions)
        {
            if (hash == sharedHash)
                continue;

            foreach (var version in versions)
            {
                if (assetInfo.Variants!.TryGetValue(version, out var variant))
                {
                    newVariants[version] = variant;
                }
            }
        }

        return new AssetInfoV3
        {
            Type = assetInfo.Type,
            Status = "partial",
            SharedHash = sharedHash,
            Size = sharedSize,
            Versions = new List<string>(sharedVersions),
            Extension = assetInfo.Extension,
            Variants = newVariants.Count > 0 ? newVariants : null
        };
    }

    public bool IsPromotionNeeded()
    {
        return _manifestManager.Manifest.Builds.Count >= 2;
    }

    public PromotionResult GetCurrentStats()
    {
        var result = new PromotionResult();
        var assets = _manifestManager.GetAllAssets();

        result.TotalAssets = assets.Count;

        foreach (var (_, assetInfo) in assets)
        {
            switch (assetInfo.Status)
            {
                case "shared":
                    result.SharedAssets++;
                    break;
                case "partial":
                    result.PartialAssets++;
                    break;
                default:
                    result.BuildSpecificAssets++;
                    break;
            }
        }

        return result;
    }
}
