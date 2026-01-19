#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AssetExtractor.Models.Manifest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Pipeline;

/// <summary>
/// Multi-version asset cache tracker. Thread-safe manifest operations via lock.
/// Tracks build info (version → hash), asset variants (file → versions), and promotion status.
/// </summary>
public class ManifestManager
{
    private readonly ILogger<ManifestManager> _logger;
    private readonly string _outputPath;
    private readonly string _manifestPath;
    private readonly object _manifestLock = new();
    private CacheManifestV3 _manifest;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ManifestManager(string outputPath, ILogger<ManifestManager>? logger = null)
    {
        _logger = logger ?? NullLogger<ManifestManager>.Instance;
        _outputPath = outputPath;
        _manifestPath = Path.Combine(outputPath, "cache_manifest.json");
        _manifest = LoadOrCreate();
    }

    public CacheManifestV3 Manifest
    {
        get
        {
            lock (_manifestLock)
            {
                return _manifest;
            }
        }
    }

    /// <summary>
    /// API reloads after CLI subprocess completes extraction.
    /// </summary>
    public void ReloadManifest()
    {
        lock (_manifestLock)
        {
            _manifest = LoadOrCreate();
        }
    }

    private CacheManifestV3 LoadOrCreate()
    {
        if (File.Exists(_manifestPath))
        {
            try
            {
                var json = File.ReadAllText(_manifestPath);
                var manifest = JsonSerializer.Deserialize<CacheManifestV3>(json, JsonOptions);

                if (manifest != null && manifest.Version == 3)
                {
                    _logger.LogInformation(
                        "Loaded manifest with {BuildCount} builds and {AssetCount} assets",
                        manifest.Builds.Count,
                        manifest.Assets.Count);
                    return manifest;
                }

                _logger.LogWarning("Manifest version mismatch, creating new manifest");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load manifest. Creating new manifest");
            }
        }

        return new CacheManifestV3();
    }

    public void SaveManifest()
    {
        lock (_manifestLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_manifestPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(_manifest, JsonOptions);
                File.WriteAllText(_manifestPath, json);

                _logger.LogInformation(
                    "Saved manifest: {BuildCount} builds, {AssetCount} assets",
                    _manifest.Builds.Count,
                    _manifest.Assets.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save manifest");
            }
        }
    }

    public void UpdateBuildInfo(string version, string gameRootPath, string assetsHash)
    {
        lock (_manifestLock)
        {
            _manifest.Builds[version] = new BuildInfo
            {
                GameVersion = version,
                GameRootPath = gameRootPath,
                ExtractedAt = DateTime.UtcNow.ToString("O"),
                AssetsHash = assetsHash
            };
        }
    }

    public BuildInfo? GetBuildInfo(string version)
    {
        lock (_manifestLock)
        {
            return _manifest.Builds.TryGetValue(version, out var info) ? info : null;
        }
    }

    public bool IsVersionExtracted(string version)
    {
        lock (_manifestLock)
        {
            return _manifest.Builds.ContainsKey(version);
        }
    }

    public void UpdateAsset(string relativePath, AssetInfoV3 assetInfo)
    {
        lock (_manifestLock)
        {
            _manifest.Assets[relativePath] = assetInfo;
        }
    }

    public AssetInfoV3? GetAsset(string relativePath)
    {
        lock (_manifestLock)
        {
            return _manifest.Assets.TryGetValue(relativePath, out var info) ? info : null;
        }
    }

    /// <summary>
    /// Handles extension collisions (same path, different extensions like .png/.glb).
    /// Without collision: key = "Assets/Texture2D/icon", extension stored in AssetInfo.
    /// With collision: keys become "Assets/Texture2D/icon.png" and "Assets/Texture2D/icon.glb".
    /// </summary>
    public void AddOrUpdateVariant(
        string relativePath,
        string version,
        string hash,
        long size,
        string assetType,
        string extension)
    {
        lock (_manifestLock)
        {
            string manifestKey = relativePath;

            if (_manifest.Assets.TryGetValue(relativePath, out var existingInfo))
            {
                if (existingInfo.Extension != extension)
                {
                    string existingKey = relativePath + existingInfo.Extension;
                    if (!_manifest.Assets.ContainsKey(existingKey))
                    {
                        _manifest.Assets[existingKey] = existingInfo;
                        _manifest.Assets.Remove(relativePath);
                    }

                    manifestKey = relativePath + extension;
                }
            }

            if (!_manifest.Assets.TryGetValue(manifestKey, out var assetInfo))
            {
                assetInfo = new AssetInfoV3
                {
                    Type = assetType,
                    Status = "build-specific",
                    Extension = extension,
                    Variants = new Dictionary<string, AssetVariantV3>()
                };
                _manifest.Assets[manifestKey] = assetInfo;
            }

            assetInfo.Variants ??= new Dictionary<string, AssetVariantV3>();

            assetInfo.Variants[version] = new AssetVariantV3
            {
                Hash = hash,
                Size = size
            };
        }
    }

    public void SetLastPromotionRun(DateTime timestamp)
    {
        lock (_manifestLock)
        {
            _manifest.LastPromotionRun = timestamp.ToString("O");
        }
    }

    public List<string> GetAllVersions()
    {
        lock (_manifestLock)
        {
            return new List<string>(_manifest.Builds.Keys);
        }
    }

    public Dictionary<string, AssetInfoV3> GetAllAssets()
    {
        lock (_manifestLock)
        {
            return new Dictionary<string, AssetInfoV3>(_manifest.Assets);
        }
    }

    /// <summary>
    /// PromotionService atomically replaces all asset entries.
    /// </summary>
    public void ReplaceAllAssets(Dictionary<string, AssetInfoV3> assets)
    {
        lock (_manifestLock)
        {
            _manifest.Assets = assets;
        }
    }

    /// <summary>
    /// Safe folder naming: "0.46.10 demo" → "0.46.10-demo" (spaces→hyphens, lowercase, filesystem-safe).
    /// </summary>
    public static string NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "unknown";

        var normalized = Regex.Replace(version.Trim(), @"\s+", "-");
        normalized = Regex.Replace(normalized, @"[<>:""/\\|?*]", "_");
        return normalized.ToLowerInvariant();
    }

    /// <summary>
    /// Priority: globalgamemanagers binary scan → XXHash64 of sharedassets0 → timestamp.
    /// Unity stores bundleVersion in globalgamemanagers (regex scan for version patterns).
    /// </summary>
    public static string DetectGameVersion(string gamePath, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        var globalGameManagers = Path.Combine(gamePath, "globalgamemanagers");

        if (File.Exists(globalGameManagers))
        {
            try
            {
                var data = File.ReadAllBytes(globalGameManagers);
                var version = TryExtractVersionFromBinary(data);

                if (!string.IsNullOrEmpty(version))
                {
                    logger.LogInformation("Detected game version: {GameVersion}", version);
                    return NormalizeVersion(version);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read version from globalgamemanagers");
            }
        }

        var sharedAssets = Path.Combine(gamePath, "sharedassets0.assets");

        if (File.Exists(sharedAssets))
        {
            var hash = DeduplicationService.ComputeFileHash(sharedAssets);
            if (!string.IsNullOrEmpty(hash))
            {
                var versionFromHash = hash.Substring(0, Math.Min(12, hash.Length));
                logger.LogInformation("Using hash-based version: {Version}", versionFromHash);
                return versionFromHash;
            }
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        logger.LogInformation("Using timestamp-based version: {Version}", timestamp);
        return timestamp;
    }

    private static string? TryExtractVersionFromBinary(byte[] data)
    {
        try
        {
            var text = System.Text.Encoding.ASCII.GetString(data);

            var versionPatterns = new[]
            {
                @"\b(\d+\.\d+\.\d+(?:[-\s]?[a-zA-Z]+)?)\b",
                @"\b(v?\d+\.\d+(?:\.\d+)?(?:[-\s]?(?:demo|alpha|beta|cb|rc))?)\b"
            };

            foreach (var pattern in versionPatterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var version = match.Groups[1].Value;

                    if (version.Length >= 3 && version.Length <= 30)
                    {
                        return version;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public string GetVersionOutputPath(string version)
    {
        return Path.Combine(_outputPath, $"Assets-{version}");
    }

    public string GetSharedOutputPath()
    {
        return Path.Combine(_outputPath, "Assets-shared");
    }

    public string OutputPath => _outputPath;
}
