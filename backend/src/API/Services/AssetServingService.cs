namespace API.Services;

public class AssetServingService : IAssetServingService
{
    private readonly TexturePathResolver _texturePathResolver;
    private readonly ILogger<AssetServingService> _logger;
    private readonly string _extractedAssetsDir;

    private readonly object _cacheLock = new();
    private List<string>? _cachedIconsList;
    private DateTime _cacheTimestamp = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public string ExtractedAssetsDirectory => _extractedAssetsDir;
    public string? CurrentVersion => _texturePathResolver.CurrentVersion;

    public AssetServingService(
        TexturePathResolver texturePathResolver,
        ILogger<AssetServingService> logger)
    {
        _texturePathResolver = texturePathResolver ?? throw new ArgumentNullException(nameof(texturePathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _extractedAssetsDir = Path.Combine(AppContext.BaseDirectory, "ExtractedAssets");

        _logger.LogInformation("AssetServingService initialized. Assets directory: {Directory}", _extractedAssetsDir);
    }

    public string? ResolveIconPath(string relativePath, string? version = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            _logger.LogDebug("ResolveIconPath called with empty path");
            return null;
        }

        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');

        if (normalizedPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath[..^4];
        }

        var resolvedPath = _texturePathResolver.ResolveIconPath(normalizedPath, version);

        if (resolvedPath != null)
        {
            _logger.LogDebug("Resolved icon path: {RelativePath} -> {ResolvedPath}", relativePath, resolvedPath);
        }
        else
        {
            _logger.LogDebug("Icon not found: {RelativePath}", relativePath);
        }

        return resolvedPath;
    }

    public bool IconExists(string relativePath)
    {
        var resolvedPath = ResolveIconPath(relativePath);
        return resolvedPath != null && File.Exists(resolvedPath);
    }

    public IReadOnlyList<string> GetExtractedIcons()
    {
        lock (_cacheLock)
        {
            if (_cachedIconsList != null && DateTime.UtcNow - _cacheTimestamp < CacheDuration)
            {
                _logger.LogDebug("Returning cached icons list ({Count} icons)", _cachedIconsList.Count);
                return _cachedIconsList.AsReadOnly();
            }

            _cachedIconsList = ScanExtractedIcons();
            _cacheTimestamp = DateTime.UtcNow;

            _logger.LogInformation("Rebuilt icons cache with {Count} icons", _cachedIconsList.Count);
            return _cachedIconsList.AsReadOnly();
        }
    }

    public void SetCurrentVersion(string? version)
    {
        _texturePathResolver.CurrentVersion = version ?? string.Empty;
        InvalidateCache();
        _logger.LogInformation("Game version set to: {Version}, caches invalidated", version ?? "(null)");
    }

    // Cache invalidation: called after asset extraction to force icon list refresh
    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedIconsList = null;
            _cacheTimestamp = DateTime.MinValue;
            _logger.LogDebug("Icon cache invalidated");
        }
    }

    private List<string> ScanExtractedIcons()
    {
        var icons = new List<string>();

        if (!Directory.Exists(_extractedAssetsDir))
        {
            _logger.LogDebug("ExtractedAssets directory does not exist: {Directory}", _extractedAssetsDir);
            return icons;
        }

        try
        {
            var assetDirs = Directory.GetDirectories(_extractedAssetsDir, "Assets-*");

            foreach (var assetDir in assetDirs)
            {
                var resourcesIconsPath = Path.Combine(assetDir, "Assets", "Resources", "icons");
                if (Directory.Exists(resourcesIconsPath))
                {
                    ScanIconDirectory(resourcesIconsPath, icons, assetDir);
                }

                var directIconsPath = Path.Combine(assetDir, "icons");
                if (Directory.Exists(directIconsPath))
                {
                    ScanIconDirectory(directIconsPath, icons, assetDir);
                }

                var texture2DPath = Path.Combine(assetDir, "Assets", "Texture2D");
                if (Directory.Exists(texture2DPath))
                {
                    ScanIconDirectory(texture2DPath, icons, assetDir);
                }

                var resourcesObjectsPath = Path.Combine(assetDir, "Assets", "Resources", "objects");
                if (Directory.Exists(resourcesObjectsPath))
                {
                    ScanIconDirectory(resourcesObjectsPath, icons, assetDir);
                }
            }

            var sharedDir = Path.Combine(_extractedAssetsDir, "Assets-shared");
            if (Directory.Exists(sharedDir))
            {
                var sharedIconsPath = Path.Combine(sharedDir, "Assets", "Resources", "icons");
                if (Directory.Exists(sharedIconsPath))
                {
                    ScanIconDirectory(sharedIconsPath, icons, sharedDir);
                }

                var sharedObjectsPath = Path.Combine(sharedDir, "Assets", "Resources", "objects");
                if (Directory.Exists(sharedObjectsPath))
                {
                    ScanIconDirectory(sharedObjectsPath, icons, sharedDir);
                }
            }

            icons = icons.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning extracted icons: {Message}", ex.Message);
        }

        return icons;
    }

    private void ScanIconDirectory(string directory, List<string> icons, string baseDir)
    {
        try
        {
            var pngFiles = Directory.GetFiles(directory, "*.png", SearchOption.AllDirectories);

            foreach (var pngFile in pngFiles)
            {
                var relativePath = Path.GetRelativePath(baseDir, pngFile);

                relativePath = relativePath
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');

                if (relativePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = relativePath[..^4];
                }

                icons.Add(relativePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning directory {Directory}: {Message}", directory, ex.Message);
        }
    }
}
