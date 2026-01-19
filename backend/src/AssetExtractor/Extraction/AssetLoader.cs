#nullable enable
using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_147; // IResourceManager
using AssetRipper.SourceGenerated.Extensions;
using AssetExtractor.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

/// <summary>
/// Separated from extraction to isolate AssetRipper dependencies and enable reuse across extractors.
/// </summary>
public sealed class AssetLoader : IDisposable
{
    private readonly ILogger<AssetLoader> _logger;
    private readonly string _assetPath;
    private readonly GameBundle _gameBundle;
    private readonly Dictionary<string, IGameObject> _prefabCache;
    private readonly Dictionary<string, IUnityObjectBase> _pathIdCache;
    private readonly Dictionary<string, IGameObject> _resourcePathCache;
    private readonly Dictionary<string, List<(string Path, IGameObject Prefab)>> _resourceNameCache;
    private bool _disposed;

    public GameBundle GameBundle => _gameBundle;
    public string AssetPath => _assetPath;
    public IReadOnlyDictionary<string, IGameObject> PrefabCache => _prefabCache;

    public AssetLoader(string assetPath, ILogger<AssetLoader>? logger = null)
    {
        _logger = logger ?? NullLogger<AssetLoader>.Instance;
        _assetPath = assetPath;

        _logger.LogInformation("Initializing AssetLoader");
        _logger.LogInformation("Asset path: {AssetPath}", assetPath);

        var assetFiles = CollectAssetFiles(assetPath);
        _logger.LogInformation("Found {AssetFileCount} asset files to load", assetFiles.Count);

        var assemblyManager = new BaseManager(_ => { });
        var assetFactory = new GameAssetFactory(assemblyManager);

        _logger.LogInformation("Loading GameBundle (this may take a moment)");
        Console.WriteLine($"Loading game assets ({assetFiles.Count} files)...");

        using (var spinner = new SpinnerDisplay("Loading"))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            _gameBundle = GameBundle.FromPaths(
                assetFiles,
                assetFactory,
                LocalFileSystem.Instance,
                null
            );

            sw.Stop();
            _logger.LogInformation("GameBundle loaded in {LoadTimeMs}ms", sw.ElapsedMilliseconds);
            spinner.Complete($"Loaded in {sw.ElapsedMilliseconds / 1000.0:F1}s");
        }
        Console.WriteLine();

        _logger.LogInformation("Building asset caches");
        _pathIdCache = new Dictionary<string, IUnityObjectBase>();
        _prefabCache = BuildPrefabCache();
        (_resourcePathCache, _resourceNameCache) = BuildResourcePathCaches();
        _logger.LogInformation("Prefab cache: {PrefabCount} entries", _prefabCache.Count);
        _logger.LogInformation("Resource path cache: {ResourcePathCount} entries", _resourcePathCache.Count);
        _logger.LogInformation("Resource name cache: {ResourceNameCount} unique names", _resourceNameCache.Count);
        _logger.LogInformation("PathID cache: {PathIdCount} entries", _pathIdCache.Count);
    }

    private static List<string> CollectAssetFiles(string assetPath)
    {
        var files = new List<string>();

        var resourcesPath = Path.Combine(assetPath, "resources.assets");
        if (File.Exists(resourcesPath))
        {
            files.Add(resourcesPath);
        }

        foreach (var file in Directory.GetFiles(assetPath, "sharedassets*.assets"))
        {
            files.Add(file);
        }

        foreach (var file in Directory.GetFiles(assetPath, "level*.assets"))
        {
            files.Add(file);
        }

        var globalPath = Path.Combine(assetPath, "globalgamemanagers");
        if (File.Exists(globalPath))
        {
            files.Add(globalPath);
        }

        var globalAssetsPath = Path.Combine(assetPath, "globalgamemanagers.assets");
        if (File.Exists(globalAssetsPath))
        {
            files.Add(globalAssetsPath);
        }

        foreach (var file in Directory.GetFiles(assetPath, "*.resS"))
        {
            files.Add(file);
        }

        foreach (var file in Directory.GetFiles(assetPath, "*.resource"))
        {
            files.Add(file);
        }

        return files;
    }

    private Dictionary<string, IGameObject> BuildPrefabCache()
    {
        var prefabCache = new Dictionary<string, IGameObject>(StringComparer.OrdinalIgnoreCase);
        var prefabCandidates = new Dictionary<string, List<IGameObject>>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in _gameBundle.FetchAssets())
        {
            if (asset.PathID != 0)
            {
                var cacheKey = MakePathIdCacheKey(asset.Collection.Name, asset.PathID);
                if (!_pathIdCache.ContainsKey(cacheKey))
                {
                    _pathIdCache[cacheKey] = asset;
                }
            }

            if (asset is not IGameObject gameObject)
            {
                continue;
            }

            if (!gameObject.TryGetComponent<ITransform>(out var transform))
            {
                continue;
            }

            if (transform.Father_C4P != null)
            {
                continue;
            }

            var name = gameObject.Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!prefabCandidates.ContainsKey(name))
            {
                prefabCandidates[name] = new List<IGameObject>();
            }
            prefabCandidates[name].Add(gameObject);
        }

        foreach (var (name, candidates) in prefabCandidates)
        {
            if (candidates.Count == 1)
            {
                prefabCache[name] = candidates[0];
            }
            else
            {
                IGameObject? best = null;
                int bestScore = -1;

                foreach (var candidate in candidates)
                {
                    int score = ScorePrefabCandidate(candidate);
                    if (score > bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }

                if (best != null)
                {
                    prefabCache[name] = best;
                }
            }
        }

        return prefabCache;
    }

    /// <summary>
    /// Multiple root GameObjects can share the same name in Unity asset bundles.
    /// Units typically have a wrapper hierarchy ("scale_roll_pos" → unit mesh) which we score higher.
    /// </summary>
    private int ScorePrefabCandidate(IGameObject gameObject)
    {
        int score = 0;

        if (!gameObject.TryGetComponent<ITransform>(out var transform))
        {
            return score;
        }

        foreach (var childTransform in transform.Children_C4P.WhereNotNull())
        {
            var childGO = childTransform.GameObject_C4P;
            if (childGO == null)
            {
                continue;
            }

            var childName = childGO.Name?.String ?? "";

            if (childName.Contains("scale_roll", StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }
            else if (childName.EndsWith("_roll", StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }
            else if (childName.Contains("wrapper", StringComparison.OrdinalIgnoreCase))
            {
                score += 800;
            }
            else if (childName.Contains("scale", StringComparison.OrdinalIgnoreCase))
            {
                score += 500;
            }

            var rot = childTransform.LocalRotation_C4;
            const float WRAPPER_Y_ROT = 0.707107f; // 45° around Y-axis, common in unit wrappers
            const float TOL = 0.0001f;
            if (Math.Abs(rot.X) < TOL &&
                Math.Abs(rot.Z) < TOL &&
                Math.Abs(Math.Abs(rot.Y) - WRAPPER_Y_ROT) < TOL &&
                Math.Abs(Math.Abs(rot.W) - WRAPPER_Y_ROT) < TOL)
            {
                score += 600;
            }
        }

        int childCount = transform.Children_C4P.Count;
        score += childCount * 10;

        return score;
    }

    private (Dictionary<string, IGameObject> PathCache, Dictionary<string, List<(string Path, IGameObject Prefab)>> NameCache) BuildResourcePathCaches()
    {
        var pathCache = new Dictionary<string, IGameObject>(StringComparer.OrdinalIgnoreCase);
        var nameCache = new Dictionary<string, List<(string Path, IGameObject Prefab)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in _gameBundle.FetchAssets())
        {
            if (asset is IResourceManager resourceManager)
            {
                foreach (var kvp in resourceManager.Container)
                {
                    var referencedAsset = kvp.Value.TryGetAsset(resourceManager.Collection);
                    if (referencedAsset is not IGameObject gameObject)
                        continue;

                    string resourceKey = kvp.Key.String;
                    if (string.IsNullOrEmpty(resourceKey))
                        continue;

                    resourceKey = resourceKey.Replace('\\', '/');

                    if (!pathCache.ContainsKey(resourceKey))
                    {
                        pathCache[resourceKey] = gameObject;
                    }

                    string prefabName = resourceKey;
                    int lastSlash = resourceKey.LastIndexOf('/');
                    if (lastSlash >= 0 && lastSlash < resourceKey.Length - 1)
                    {
                        prefabName = resourceKey[(lastSlash + 1)..];
                    }

                    if (!nameCache.TryGetValue(prefabName, out var list))
                    {
                        list = new List<(string Path, IGameObject Prefab)>();
                        nameCache[prefabName] = list;
                    }

                    if (!list.Any(x => x.Prefab.PathID == gameObject.PathID))
                    {
                        list.Add((resourceKey, gameObject));
                    }
                }
            }
        }

        return (pathCache, nameCache);
    }

    private static string MakePathIdCacheKey(string sourceFile, long pathId) => $"{sourceFile}:{pathId}";

    #region Lookup Methods

    public List<string> ListResourcePaths(string? pattern = null)
    {
        var paths = _resourcePathCache.Keys.AsEnumerable();

        if (!string.IsNullOrEmpty(pattern))
        {
            paths = paths.Where(p => p.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        return paths.OrderBy(p => p).ToList();
    }

    public IGameObject? FindPrefabByNameWithResourcePaths(string prefabName, string? pathPrefix = null)
    {
        if (string.IsNullOrEmpty(prefabName))
            return null;

        if (!_resourceNameCache.TryGetValue(prefabName, out var candidates))
            return null;

        if (candidates.Count == 0)
            return null;

        if (string.IsNullOrEmpty(pathPrefix))
        {
            return candidates[0].Prefab;
        }

        var filtered = candidates.Where(c => c.Path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (filtered.Count == 0)
            return null;

        var prefabPath = filtered.FirstOrDefault(c => c.Path.Contains("/1prefabs/", StringComparison.OrdinalIgnoreCase));
        if (prefabPath.Prefab != null)
            return prefabPath.Prefab;

        return filtered[0].Prefab;
    }

    public IGameObject? FindPrefabByResourcePath(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
            return null;

        resourcePath = resourcePath.Replace('\\', '/');

        if (_resourcePathCache.TryGetValue(resourcePath, out var prefab))
        {
            return prefab;
        }

        if (!resourcePath.StartsWith("objects/", StringComparison.OrdinalIgnoreCase))
        {
            if (_resourcePathCache.TryGetValue($"objects/{resourcePath}", out prefab))
            {
                return prefab;
            }
        }

        return null;
    }

    public IGameObject? FindGameObjectByPathId(string sourceFile, long pathId)
    {
        var cacheKey = MakePathIdCacheKey(sourceFile, pathId);
        if (_pathIdCache.TryGetValue(cacheKey, out var asset))
        {
            return asset as IGameObject;
        }
        return null;
    }

    public IGameObject? FindPrefabByName(string prefabName)
    {
        if (_prefabCache.TryGetValue(prefabName, out var prefab))
        {
            return prefab;
        }

        var variantInfo = HierarchySelector.ParseUnitVariant(prefabName);
        if (variantInfo.Kind != UnitVariantKind.Base)
        {
            if (_prefabCache.TryGetValue(variantInfo.BaseName, out prefab))
            {
                return prefab;
            }
        }

        return null;
    }

    public List<string> ListAllPrefabNames()
    {
        return _prefabCache.Keys.OrderBy(k => k).ToList();
    }

    #endregion

    #region Debug Methods

    public void DebugPrefabHierarchy(string prefabName)
    {
        if (prefabName.StartsWith("#"))
        {
            Console.WriteLine("Direct PathID lookup not supported. Use prefab name instead");
            return;
        }

        IGameObject? prefabRoot = null;
        if (prefabName.Contains('/'))
        {
            prefabRoot = FindPrefabByResourcePath(prefabName);
            if (prefabRoot != null)
            {
                Console.WriteLine($"Found by resource path (PathID: {prefabRoot.PathID})");
            }
        }

        if (prefabRoot == null)
        {
            prefabRoot = FindPrefabByNameWithResourcePaths(prefabName);
            if (prefabRoot != null)
            {
                Console.WriteLine($"Found by resource name cache (PathID: {prefabRoot.PathID})");
            }
        }

        if (prefabRoot == null)
        {
            prefabRoot = FindPrefabByName(prefabName);
            if (prefabRoot != null)
            {
                Console.WriteLine($"Found by name cache (PathID: {prefabRoot.PathID})");
            }
        }

        if (prefabRoot == null)
        {
            Console.WriteLine($"Prefab not found: {prefabName}");
            return;
        }

        DebugGameObjectHierarchy(prefabRoot);
    }

    private void DebugGameObjectHierarchy(IGameObject gameObject)
    {
        var collection = gameObject.Collection;
        var assetFile = collection?.Name ?? "Unknown";
        Console.WriteLine($"=== {gameObject.Name} ===");
        Console.WriteLine($"Asset File: {assetFile}");
        Console.WriteLine($"PathID: {gameObject.PathID}");
        Console.WriteLine("\nHierarchy:\n");

        if (!gameObject.TryGetComponent<ITransform>(out var rootTransform))
        {
            Console.WriteLine("ERROR: No transform on root");
            return;
        }

        PrintHierarchyRecursive(gameObject, rootTransform, 0);
    }

    private void PrintHierarchyRecursive(IGameObject gameObject, ITransform transform, int depth)
    {
        string indent = new string(' ', depth * 2);

        var components = new List<string>();
        if (gameObject.TryGetComponent<AssetRipper.SourceGenerated.Classes.ClassID_137.ISkinnedMeshRenderer>(out _))
            components.Add("SkinnedMeshRenderer");
        if (gameObject.TryGetComponent<AssetRipper.SourceGenerated.Classes.ClassID_23.IMeshRenderer>(out _))
            components.Add("MeshRenderer");
        if (gameObject.TryGetComponent<AssetRipper.SourceGenerated.Classes.ClassID_33.IMeshFilter>(out _))
            components.Add("MeshFilter");
        if (gameObject.TryGetComponent<AssetRipper.SourceGenerated.Classes.ClassID_95.IAnimator>(out _))
            components.Add("Animator");

        string componentStr = components.Count > 0 ? $" [{string.Join(", ", components)}]" : "";

        var scale = transform.LocalScale_C4;
        var rot = transform.LocalRotation_C4;
        string transformStr = "";

        if (Math.Abs(scale.X - 1) > 0.001f || Math.Abs(scale.Y - 1) > 0.001f || Math.Abs(scale.Z - 1) > 0.001f)
        {
            transformStr += $" S({scale.X:F2},{scale.Y:F2},{scale.Z:F2})";
        }

        if (Math.Abs(rot.X) > 0.001f || Math.Abs(rot.Y) > 0.001f || Math.Abs(rot.Z) > 0.001f || Math.Abs(rot.W - 1) > 0.001f)
        {
            transformStr += $" R({rot.X:F2},{rot.Y:F2},{rot.Z:F2},{rot.W:F2})";
        }

        Console.WriteLine($"{indent}- {gameObject.Name}{componentStr}{transformStr}");

        foreach (var childTrans in transform.Children_C4P.WhereNotNull())
        {
            var childGo = childTrans.GameObject_C4P;
            if (childGo != null)
            {
                PrintHierarchyRecursive(childGo, childTrans, depth + 1);
            }
        }
    }

    public void AnalyzeAssetStructure(string searchTerm)
    {
        Console.WriteLine("=== Asset Bundle Structure ===\n");
        Console.WriteLine("Asset Files Loaded:");

        var collections = _gameBundle.FetchAssetCollections().ToList();
        for (int i = 0; i < collections.Count; i++)
        {
            var collection = collections[i];
            Console.WriteLine($"  {i + 1}. {collection.Name}");
        }

        Console.WriteLine($"\nTotal: {collections.Count} asset files\n");

        Console.WriteLine($"=== Searching for assets matching '{searchTerm}' ===\n");

        var matches = new List<(string Type, string Name, long PathID, string Collection)>();

        foreach (var asset in _gameBundle.FetchAssets())
        {
            string assetName = asset.ToString() ?? "";
            string typeName = asset.GetType().Name;

            if (asset is AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D texture)
            {
                assetName = texture.Name_C28?.String ?? assetName;
            }
            else if (asset is IGameObject go)
            {
                assetName = go.Name ?? assetName;
            }

            if (assetName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((typeName, assetName, asset.PathID, asset.Collection.Name));
            }
        }

        if (matches.Count == 0)
        {
            Console.WriteLine($"No assets found matching '{searchTerm}'");
        }
        else
        {
            Console.WriteLine($"Found {matches.Count} matches:\n");
            foreach (var (type, name, pathId, collection) in matches.Take(50))
            {
                Console.WriteLine($"  - {type}: {name}");
                Console.WriteLine($"    PathID: {pathId}, File: {collection}");
            }

            if (matches.Count > 50)
            {
                Console.WriteLine($"\n  ... and {matches.Count - 50} more");
            }
        }
    }

    public List<(string Name, AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D Texture)> SearchTextures(string namePattern)
    {
        var textures = new List<(string Name, AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D Texture)>();

        foreach (var asset in _gameBundle.FetchAssets())
        {
            if (asset is AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D texture)
            {
                var nameStr = texture.Name?.String ?? "";
                if (nameStr.Contains(namePattern, StringComparison.OrdinalIgnoreCase))
                {
                    textures.Add((nameStr, texture));
                }
            }
        }

        return textures;
    }

    /// <summary>
    /// Material references to textures are sometimes null in Unity asset bundles (authoring issue or stripped data).
    /// Fallback searches by conventional path patterns used by the game (e.g., "objects/artifact/models/book_artifact/book_texture").
    /// </summary>
    public AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D? FindTextureByResourcePath(string resourcePathPattern)
    {
        if (string.IsNullOrEmpty(resourcePathPattern))
            return null;

        resourcePathPattern = resourcePathPattern.Replace('\\', '/');

        foreach (var asset in _gameBundle.FetchAssets())
        {
            if (asset is IResourceManager resourceManager)
            {
                foreach (var kvp in resourceManager.Container)
                {
                    string resourceKey = kvp.Key.String.Replace('\\', '/');

                    if (resourceKey.Equals(resourcePathPattern, StringComparison.OrdinalIgnoreCase))
                    {
                        var referencedAsset = kvp.Value.TryGetAsset(resourceManager.Collection);
                        if (referencedAsset is AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D texture)
                        {
                            return texture;
                        }
                    }

                    if (resourceKey.Contains(resourcePathPattern, StringComparison.OrdinalIgnoreCase))
                    {
                        var referencedAsset = kvp.Value.TryGetAsset(resourceManager.Collection);
                        if (referencedAsset is AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D texture)
                        {
                            return texture;
                        }
                    }
                }
            }
        }

        return null;
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _gameBundle?.Dispose();
            _disposed = true;
        }
    }
}
