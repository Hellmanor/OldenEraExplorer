using Microsoft.Extensions.Logging;

namespace API.Services;

public class TexturePathResolver
{
    private readonly string _extractedAssetsDir;
    private readonly string _customAssetsDir;
    private readonly ILogger<TexturePathResolver> _logger;

    public string CurrentVersion { get; set; } = string.Empty;

    public TexturePathResolver(string executableDirectory, ILogger<TexturePathResolver> logger)
    {
        _extractedAssetsDir = Path.Combine(executableDirectory, "ExtractedAssets");
        _customAssetsDir = Path.Combine(executableDirectory, "CustomAssets");
        _logger = logger;
    }

    public string? ResolveIconPath(string relativePath, string? version = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');

        if (!normalizedPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath += ".png";
        }

        var searchPaths = BuildSearchPaths(normalizedPath, version ?? CurrentVersion);

        foreach (var searchPath in searchPaths)
        {
            var resolved = TryResolvePath(searchPath);
            if (resolved != null)
            {
                _logger.LogDebug("Resolved icon path: {RelativePath} -> {Path}", relativePath, resolved);
                return resolved;
            }
        }

        _logger.LogDebug("Icon not found: {RelativePath}", relativePath);
        return null;
    }

    private string? TryResolvePath(string searchPath)
    {
        if (searchPath.Contains("*"))
        {
            return FindFileWithWildcard(searchPath);
        }

        if (File.Exists(searchPath))
            return searchPath;

        return FindFileCaseInsensitive(searchPath);
    }

    private string? FindFileWithWildcard(string wildcardPath)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(wildcardPath);
            var filePattern = Path.GetFileName(wildcardPath);

            if (string.IsNullOrEmpty(directoryPath) || string.IsNullOrEmpty(filePattern))
                return null;

            var resolvedDirectory = FindDirectoryCaseInsensitive(directoryPath);
            if (resolvedDirectory == null || !Directory.Exists(resolvedDirectory))
                return null;

            var matchingFiles = Directory.GetFiles(resolvedDirectory, filePattern, SearchOption.TopDirectoryOnly);

            if (matchingFiles.Length > 0)
            {
                _logger.LogDebug("Wildcard match found: {Pattern} -> {File}", wildcardPath, matchingFiles[0]);
                return matchingFiles[0];
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching for wildcard path: {Path}", wildcardPath);
            return null;
        }
    }

    private static string? FindDirectoryCaseInsensitive(string directoryPath)
    {
        var segments = directoryPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        var currentPath = Path.IsPathRooted(directoryPath) ? Path.GetPathRoot(directoryPath)! : ".";

        foreach (var segment in segments)
        {
            if (!Directory.Exists(currentPath))
                return null;

            string? match = null;
            try
            {
                var entries = Directory.GetDirectories(currentPath);
                foreach (var entry in entries)
                {
                    var entryName = Path.GetFileName(entry);
                    if (string.Equals(entryName, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        match = entry;
                        break;
                    }
                }
            }
            catch
            {
                return null;
            }

            if (match == null)
                return null;

            currentPath = match;
        }

        return Directory.Exists(currentPath) ? currentPath : null;
    }

    private static string? FindFileCaseInsensitive(string fullPath)
    {
        var segments = fullPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        var currentPath = Path.IsPathRooted(fullPath) ? Path.GetPathRoot(fullPath)! : ".";

        foreach (var segment in segments)
        {
            if (!Directory.Exists(currentPath))
                return null;

            string? match = null;
            try
            {
                var entries = Directory.GetFileSystemEntries(currentPath);
                foreach (var entry in entries)
                {
                    var entryName = Path.GetFileName(entry);
                    if (string.Equals(entryName, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        match = entry;
                        break;
                    }
                }
            }
            catch
            {
                return null;
            }

            if (match == null)
                return null;

            currentPath = match;
        }

        return File.Exists(currentPath) ? currentPath : null;
    }

    private List<string> BuildSearchPaths(string normalizedPath, string version)
    {
        var paths = new List<string>();
        var pathVariants = new List<string> { normalizedPath };

        if (!normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            pathVariants.Add($"Assets/Resources/{normalizedPath}");
            pathVariants.Add($"Assets/Texture2D/{normalizedPath}");
        }

        if (normalizedPath.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase))
        {
            pathVariants.Add(normalizedPath.Substring("Assets/Resources/".Length));
        }

        if (normalizedPath.Contains("hero_large_portraits") && normalizedPath.Contains("_large"))
        {
            var smallPortraitPath = normalizedPath
                .Replace("hero_large_portraits", "heroes")
                .Replace("_large.png", ".png");

            pathVariants.Add(smallPortraitPath);

            if (!smallPortraitPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                pathVariants.Add($"Assets/Resources/{smallPortraitPath}");
            }
        }

        var sharedDir = Path.Combine(_extractedAssetsDir, "Assets-shared");
        foreach (var variant in pathVariants)
        {
            paths.Add(Path.Combine(sharedDir, variant));
        }

        if (!string.IsNullOrEmpty(version))
        {
            var versionDir = Path.Combine(_extractedAssetsDir, $"Assets-{version}");
            foreach (var variant in pathVariants)
            {
                paths.Add(Path.Combine(versionDir, variant));
            }
        }

        if (Directory.Exists(_extractedAssetsDir))
        {
            try
            {
                var assetDirs = Directory.GetDirectories(_extractedAssetsDir, "Assets-*")
                    .Where(d => !d.EndsWith("Assets-shared"))
                    .OrderByDescending(d => d);

                foreach (var assetDir in assetDirs)
                {
                    foreach (var variant in pathVariants)
                    {
                        paths.Add(Path.Combine(assetDir, variant));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning asset directories");
            }
        }

        var iconFileName = Path.GetFileName(normalizedPath);
        paths.Add(Path.Combine(_customAssetsDir, iconFileName));

        var folder = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder))
        {
            var lastFolder = folder.Split('/').LastOrDefault();
            if (!string.IsNullOrEmpty(lastFolder))
            {
                paths.Add(Path.Combine(_customAssetsDir, lastFolder, iconFileName));
            }
        }

        return paths;
    }

    public IEnumerable<string> ListIcons()
    {
        if (!Directory.Exists(_extractedAssetsDir))
        {
            _logger.LogDebug("ExtractedAssets directory does not exist: {Directory}", _extractedAssetsDir);
            return Enumerable.Empty<string>();
        }

        var icons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var assetDirs = Directory.GetDirectories(_extractedAssetsDir, "Assets-*");

            foreach (var assetDir in assetDirs)
            {
                ScanForIcons(assetDir, icons);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing icons: {Message}", ex.Message);
        }

        return icons;
    }

    private void ScanForIcons(string baseDir, HashSet<string> icons)
    {
        var searchPaths = new[]
        {
            Path.Combine(baseDir, "Assets", "Resources", "icons"),
            Path.Combine(baseDir, "Assets", "Resources", "objects"),
            Path.Combine(baseDir, "Assets", "Texture2D"),
            Path.Combine(baseDir, "icons")
        };

        foreach (var searchPath in searchPaths)
        {
            if (!Directory.Exists(searchPath))
                continue;

            try
            {
                var pngFiles = Directory.GetFiles(searchPath, "*.png", SearchOption.AllDirectories);

                foreach (var pngFile in pngFiles)
                {
                    var relativePath = Path.GetRelativePath(baseDir, pngFile)
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
                _logger.LogWarning(ex, "Error scanning directory {Path}", searchPath);
            }
        }
    }
}
