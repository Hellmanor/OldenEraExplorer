#nullable enable
using AssetExtractor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Providers;

public class MapObjectListProvider : IPrefabListProvider
{
    private readonly ILogger<MapObjectListProvider> _logger;
    private readonly string _assetPath;

    public MapObjectListProvider(string assetPath, ILogger<MapObjectListProvider>? logger = null)
    {
        _logger = logger ?? NullLogger<MapObjectListProvider>.Instance;
        _assetPath = assetPath;
    }

    public PrefabType PrefabType => PrefabType.MapObject;

    public List<PrefabDescriptor> ListPrefabs()
    {
        var prefabs = new List<PrefabDescriptor>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var globalGameManagersPath = Path.Combine(_assetPath, "globalgamemanagers");
        if (!File.Exists(globalGameManagersPath))
            globalGameManagersPath = Path.Combine(_assetPath, "globalgamemanagers.assets");

        if (!File.Exists(globalGameManagersPath))
            throw new FileNotFoundException($"globalgamemanagers not found under asset path: {_assetPath}");

        foreach (var token in EnumeratePrintableStrings(globalGameManagersPath, minLength: 8))
        {
            if (TryParseMapObjectPrefabDescriptor(token, out var descriptor))
            {
                string key = $"{descriptor.Category}/{descriptor.Name}";
                if (!seenKeys.Contains(key))
                {
                    seenKeys.Add(key);
                    prefabs.Add(descriptor);
                }
            }
        }

        if (prefabs.Count == 0)
        {
            throw new Exception(
                "No map object prefab paths found in globalgamemanagers. Expected strings like " +
                "'objects/<category>/<name>' in the Unity data files.");
        }

        _logger.LogInformation(
            "Loaded {MapObjectCount} map objects from {GlobalGameManagersPath}",
            prefabs.Count,
            globalGameManagersPath);

        return prefabs
            .OrderBy(p => p.Category, StringComparer.Ordinal)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    public List<string> ListMapObjects()
    {
        return ListPrefabs()
            .Select(p => $"{p.Category}/{p.Name}")
            .ToList();
    }

    private static IEnumerable<string> EnumeratePrintableStrings(string filePath, int minLength)
    {
        using var stream = File.OpenRead(filePath);

        byte[] buffer = new byte[64 * 1024];
        var currentBytes = new List<byte>();

        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < bytesRead; i++)
            {
                byte b = buffer[i];
                if ((b >= 0x20 && b <= 0x7E) || (b >= 0x80 && b <= 0xF7))
                {
                    currentBytes.Add(b);
                    continue;
                }

                if (currentBytes.Count >= minLength)
                {
                    var str = TryDecodeUtf8(currentBytes);
                    if (str != null && str.Length >= minLength)
                        yield return str;
                }

                currentBytes.Clear();
            }
        }

        if (currentBytes.Count >= minLength)
        {
            var str = TryDecodeUtf8(currentBytes);
            if (str != null && str.Length >= minLength)
                yield return str;
        }
    }

    private static string? TryDecodeUtf8(List<byte> bytes)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseMapObjectPrefabDescriptor(string assetPath, out PrefabDescriptor descriptor)
    {
        descriptor = new PrefabDescriptor();

        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        string normalized = assetPath.Trim().Replace('\\', '/');
        string lower = normalized.ToLowerInvariant();

        const string marker = "objects/";
        int objectsIndex = lower.IndexOf(marker, StringComparison.Ordinal);
        if (objectsIndex < 0)
            return false;

        int categoryStart = objectsIndex + marker.Length;
        string remainder = normalized[categoryStart..];

        var parts = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        string category = parts[0].Trim();
        string name = parts[1].Trim();

        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(name))
            return false;

        if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            name = name[..^".prefab".Length];

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (char c in category)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                continue;
            return false;
        }

        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                continue;
            return false;
        }

        var lowerCategory = category.ToLowerInvariant();
        var lowerName = name.ToLowerInvariant();

        if (IsUnwantedCategory(lowerCategory))
            return false;

        if (lowerName.EndsWith("_map", StringComparison.Ordinal) ||
            lowerName.EndsWith("_mt", StringComparison.Ordinal) ||
            lowerName.EndsWith("_mat", StringComparison.Ordinal))
        {
            return false;
        }

        if (lowerName == "models" ||
            lowerName == "universal_animation" ||
            lowerName.EndsWith("_barracks", StringComparison.Ordinal))
        {
            return false;
        }

        descriptor = new PrefabDescriptor
        {
            Name = lowerName,
            Type = PrefabType.MapObject,
            Category = lowerCategory,
            ResourcePath = normalized
        };

        return true;
    }

    private static bool IsUnwantedCategory(string category)
    {
        return category == "banners" ||
               category == "environment" ||
               category == "fx" ||
               category == "quest_markers" ||
               category == "test" ||
               category == "test_images" ||
               category == "spawners";
    }
}
