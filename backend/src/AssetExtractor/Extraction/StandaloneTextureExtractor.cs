#nullable enable
using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.SourceGenerated.Classes.ClassID_28;  // ITexture2D
using AssetRipper.SourceGenerated.Classes.ClassID_89;  // ICubemap
using AssetRipper.SourceGenerated.Classes.ClassID_147; // IResourceManager
using AssetRipper.SourceGenerated.Extensions;          // TryGetAsset extension
using AssetExtractor.Models;
using AssetExtractor.Export;
using AssetExtractor.Pipeline;
using AssetExtractor.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AssetExtractor.Extraction;

public class StandaloneTextureExtractor : IDisposable
{
    private readonly ILogger<StandaloneTextureExtractor> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly string _assetPath;
    private readonly string _outputPath;
    private GameBundle? _gameBundle;
    private readonly bool _externalGameBundle;  // Track if GameBundle was provided externally
    private bool _disposed;

    // Thread-safe progress callback (called from parallel threads)
    public Action<ExtractionProgress>? OnProgress { get; set; }

    public StandaloneTextureExtractor(
        string assetPath,
        string outputPath,
        ILogger<StandaloneTextureExtractor>? logger = null,
        ILoggerFactory? loggerFactory = null)
    {
        _logger = logger ?? NullLogger<StandaloneTextureExtractor>.Instance;
        _loggerFactory = loggerFactory;
        _assetPath = assetPath;
        _outputPath = outputPath;
        _externalGameBundle = false;
    }

    // Reuses existing GameBundle to avoid reload (~15s savings)
    public StandaloneTextureExtractor(
        string assetPath,
        string outputPath,
        GameBundle existingGameBundle,
        ILogger<StandaloneTextureExtractor>? logger = null,
        ILoggerFactory? loggerFactory = null)
    {
        _logger = logger ?? NullLogger<StandaloneTextureExtractor>.Instance;
        _loggerFactory = loggerFactory;
        _assetPath = assetPath;
        _outputPath = outputPath;
        _gameBundle = existingGameBundle ?? throw new ArgumentNullException(nameof(existingGameBundle));
        _externalGameBundle = true;
    }

    public ExtractionStats ExtractTexturesVersioned(
        string version,
        ManifestManager manifestService)
    {
        var stats = new ExtractionStats();

        try
        {
            _logger.LogInformation(
                "Starting standalone texture extraction for version: {Version}",
                version);

            // Initialize GameBundle
            InitializeGameBundle();

            // Set OriginalPath on all assets using IResourceManager.Container
            // This is what AssetRipper's EditorFormatProcessor does internally
            SetOriginalPathsFromResourceManager();

            // Create texture exporter with version management
            var textureExporter = new TextureExporter(_outputPath, manifestService, _loggerFactory?.CreateLogger<TextureExporter>());

            // Collect all Texture2D assets
            _logger.LogInformation("Collecting all Texture2D assets");
            var allTextures = CollectAllTextures();
            stats.TotalCount = allTextures.Count;

            _logger.LogInformation(
                "Found {TextureCount} Texture2D assets",
                allTextures.Count);

            // Pre-filter textures: valid dimensions + allowed paths
            _logger.LogInformation("Filtering textures");
            var texturesToExtract = allTextures
                .Where(t => t.Width_C28 > 0 && t.Height_C28 > 0)
                .Select(t => (Texture: t, RelativePath: ExtractTexturePath(t)))
                .Where(x => IsAllowedTexturePath(x.RelativePath))
                .ToList();

            int skippedCount = allTextures.Count - texturesToExtract.Count;
            _logger.LogInformation(
                "Filtered textures: {ToExtractCount} to extract, {SkippedCount} skipped (filtered)",
                texturesToExtract.Count,
                skippedCount);

            // Extract each texture (PARALLEL)
            int successCount = 0;
            int failedCount = 0;
            int processedCount = 0;

            // Use ParallelOptions to control degree of parallelism
            // FPng is thread-safe (read-only static lookup tables after init)
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            _logger.LogInformation(
                "Using {ThreadCount} threads for texture extraction",
                parallelOptions.MaxDegreeOfParallelism);

            Console.WriteLine("Extracting Textures...");
            using var progressBar = new ProgressBar("Textures", texturesToExtract.Count, OnProgress);

            Parallel.For(0, texturesToExtract.Count, parallelOptions, index =>
            {
                var (texture, relativePath) = texturesToExtract[index];

                try
                {
                    // Progress indicator (thread-safe)
                    int currentProcessed = Interlocked.Increment(ref processedCount);

                    // Get texture name for progress display
                    var textureName = texture.OriginalName ?? texture.Name ?? $"Texture_{texture.PathID}";

                    // Update progress bar every 10 items or on first/last item
                    if (currentProcessed % 10 == 0 || currentProcessed == 1 || currentProcessed == texturesToExtract.Count)
                    {
                        progressBar.Update(currentProcessed, textureName);
                    }

                    // Export with version management (TextureExporter is thread-safe via DeduplicationService)
                    var exportedPath = textureExporter.ExportTexture(texture, relativePath, version);

                    if (exportedPath != null)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref failedCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to extract texture at index {TextureIndex}",
                        index);
                    Interlocked.Increment(ref failedCount);
                }
            });

            progressBar.Complete();

            stats.SuccessCount = successCount;
            stats.FailedCount = failedCount;
            stats.SkippedCount = skippedCount;

            _logger.LogInformation("=== Texture Extraction Complete ===");
            _logger.LogInformation("Total textures found: {TotalCount}", stats.TotalCount);
            _logger.LogInformation("Successfully extracted: {SuccessCount}", stats.SuccessCount);
            _logger.LogInformation("Failed: {FailedCount}", stats.FailedCount);
            _logger.LogInformation("Skipped (filtered + invalid): {SkippedCount}", stats.SkippedCount);
            _logger.LogInformation("  - icons/ (filtered), objects/ (artifact, barracks, interactive, resource)");
            _logger.LogInformation(
                "Success rate: {SuccessRate:F2}%",
                stats.SuccessCount * 100.0 / Math.Max(1, stats.TotalCount - stats.SkippedCount));

            // Extract cubemaps (environment maps)
            ExtractCubemaps(version, textureExporter, stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Texture extraction failed");
            throw;
        }

        return stats;
    }

    private void InitializeGameBundle()
    {
        if (_gameBundle != null)
        {
            // GameBundle already exists (provided externally or already loaded)
            if (_externalGameBundle)
            {
                _logger.LogInformation("Reusing existing GameBundle for texture extraction (skipping ~15s reload)");
            }
            return;
        }

        _logger.LogInformation("Initializing GameBundle for texture extraction");

        // Collect all asset files
        var assetFiles = CollectAssetFiles(_assetPath);
        _logger.LogInformation(
            "Found {AssetFileCount} asset files to load",
            assetFiles.Count);

        // Create asset factory
        var assemblyManager = new BaseManager(_ => { });
        var assetFactory = new GameAssetFactory(assemblyManager);

        // Load GameBundle
        // Show visible loading message (always visible)
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

            _logger.LogInformation(
                "GameBundle loaded in {LoadTimeMs}ms",
                sw.ElapsedMilliseconds);

            spinner.Complete($"Loaded in {sw.ElapsedMilliseconds / 1000.0:F1}s");
        }
        Console.WriteLine();  // Empty line for spacing
    }

    // AssetRipper workaround: IResourceManager maps PathID -> resource path for unique texture paths
    private void SetOriginalPathsFromResourceManager()
    {
        if (_gameBundle == null)
            return;

        int pathsSet = 0;

        // Find all IResourceManager assets and set paths on their referenced assets
        foreach (var asset in _gameBundle.FetchAssets())
        {
            if (asset is IResourceManager resourceManager)
            {
                // Iterate IResourceManager.Container which maps resource keys to asset pointers
                foreach (var kvp in resourceManager.Container)
                {
                    var referencedAsset = kvp.Value.TryGetAsset(resourceManager.Collection);
                    if (referencedAsset == null)
                        continue;

                    // Build resource path: "Assets/Resources/{key}"
                    // Use forward slashes consistently for cross-platform compatibility
                    string resourcePath = $"Assets/Resources/{kvp.Key.String}".Replace('\\', '/');

                    // Only set if not already set, or if current path is shorter (edge case)
                    if (referencedAsset.OriginalPath == null)
                    {
                        referencedAsset.OriginalPath = resourcePath;
                        pathsSet++;
                    }
                    else if (referencedAsset.OriginalPath.Length < resourcePath.Length)
                    {
                        // For nested resources paths, prefer the longer (more specific) path
                        referencedAsset.OriginalPath = resourcePath;
                    }
                }
            }
        }

        _logger.LogInformation(
            "Set OriginalPath on {AssetCount} assets from IResourceManager",
            pathsSet);
    }

    private List<ITexture2D> CollectAllTextures()
    {
        if (_gameBundle == null)
            throw new InvalidOperationException("GameBundle not initialized");

        var textures = new List<ITexture2D>();

        foreach (var asset in _gameBundle.FetchAssets())
        {
            if (asset is ITexture2D texture)
            {
                textures.Add(texture);
            }
        }

        return textures;
    }

    private static readonly HashSet<string> AllowedCubemapNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cold Sunset Equirect"
    };

    // Cubemaps: 6 faces extracted as vertical strip for Three.js compatibility
    private void ExtractCubemaps(string version, TextureExporter textureExporter, ExtractionStats stats)
    {
        if (_gameBundle == null)
            return;

        _logger.LogInformation("Extracting cubemaps");

        var cubemaps = new List<ICubemap>();
        foreach (var asset in _gameBundle.FetchAssets())
        {
            if (asset is ICubemap cubemap)
            {
                var name = cubemap.Name ?? "";
                if (AllowedCubemapNames.Contains(name))
                {
                    cubemaps.Add(cubemap);
                }
            }
        }

        if (cubemaps.Count == 0)
        {
            _logger.LogInformation("No matching cubemaps found");
            return;
        }

        _logger.LogInformation("Found {CubemapCount} cubemaps to extract", cubemaps.Count);

        foreach (var cubemap in cubemaps)
        {
            var name = cubemap.Name ?? "UnknownCubemap";
            using (_logger.BeginScope("Cubemap: {CubemapName}", name))
            {
                try
                {
                    _logger.LogInformation("Extracting cubemap: {CubemapName}", name);

                // ICubemap inherits from ITexture2D, cast to access common properties
                var texture = cubemap as ITexture2D;
                if (texture == null)
                {
                    _logger.LogWarning("Cubemap is not ITexture2D: {CubemapName}", name);
                    continue;
                }

                // Get cubemap dimensions (each face is square)
                int faceSize = texture.Width_C28;
                if (faceSize <= 0)
                {
                    _logger.LogWarning(
                        "Invalid cubemap size: {FaceSize} for {CubemapName}",
                        faceSize,
                        name);
                    continue;
                }

                // Get image data (6 faces concatenated)
                // Thread-safe: lock around GetImageData() - reads from shared streams
                byte[] rawData;
                lock (TextureExporter.TextureConversionLock)
                {
                    rawData = texture.GetImageData();
                }
                if (rawData == null || rawData.Length == 0)
                {
                    _logger.LogWarning("No image data for cubemap: {CubemapName}", name);
                    continue;
                }

                // Export as vertical strip (6 faces stacked)
                var relativePath = $"Assets/Cubemap/{SanitizeFileName(name)}";
                var pngData = ConvertCubemapToPng(rawData, faceSize, texture.Format_C28E);

                if (pngData != null)
                {
                    var textureData = new TextureData
                    {
                        Name = name,
                        Width = faceSize * 2,  // Equirectangular: 2:1 ratio
                        Height = faceSize,
                        ImageData = pngData,
                        Format = "PNG"
                    };

                    var exportedPath = textureExporter.ExportTexture(textureData, relativePath, version);
                    if (exportedPath != null)
                    {
                        _logger.LogInformation("Exported cubemap: {CubemapPath}", exportedPath);
                        stats.SuccessCount++;
                    }
                    else
                    {
                        stats.FailedCount++;
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to convert cubemap: {CubemapName}", name);
                    stats.FailedCount++;
                }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract cubemap");
                    stats.FailedCount++;
                }
            }
        }
    }

    // Unity (left-handed) → OpenGL/Three.js (right-handed) equirectangular conversion
    // Input: 6 faces vertical strip (+X, -X, +Y, -Y, +Z, -Z)
    // Output: Equirectangular 2:1 aspect ratio
    private byte[]? ConvertCubemapToPng(byte[] rawData, int faceSize, AssetRipper.SourceGenerated.Enums.TextureFormat format)
    {
        try
        {
            // Decode the vertical strip (6 faces)
            int totalPixels = 6 * faceSize * faceSize;
            int stripSize = totalPixels * 4; // RGBA
            byte[] stripData = new byte[stripSize];

            int bytesDecoded = DecodeTextureData(rawData, faceSize, faceSize * 6, format, stripData);
            if (bytesDecoded < 0)
            {
                _logger.LogWarning("Cubemap decoding failed for format: {TextureFormat}", format);
                return null;
            }

            // Load as vertical strip - no flip needed, we handle orientation in UV mapping
            using var stripImage = Image.LoadPixelData<Rgba32>(stripData, faceSize, faceSize * 6);

            // Extract 6 faces from strip (Unity order: +X, -X, +Y, -Y, +Z, -Z)
            // Face indices: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
            var faces = new Image<Rgba32>[6];
            for (int i = 0; i < 6; i++)
            {
                faces[i] = stripImage.Clone(ctx => ctx.Crop(new Rectangle(0, i * faceSize, faceSize, faceSize)));
            }

            // Create equirectangular output (2:1 ratio)
            int outWidth = faceSize * 2;
            int outHeight = faceSize;
            using var equirect = new Image<Rgba32>(outWidth, outHeight);

            // Convert each pixel
            for (int y = 0; y < outHeight; y++)
            {
                // Latitude: PI/2 at top (y=0), -PI/2 at bottom
                double lat = (0.5 - (double)y / outHeight) * Math.PI;

                for (int x = 0; x < outWidth; x++)
                {
                    // Longitude: -PI at left, PI at right
                    double lon = ((double)x / outWidth - 0.5) * 2 * Math.PI;

                    // Spherical to cartesian (OpenGL convention: Y-up, right-handed)
                    double dx = Math.Cos(lat) * Math.Sin(lon);
                    double dy = Math.Sin(lat);
                    double dz = Math.Cos(lat) * Math.Cos(lon);

                    // Unity is left-handed, OpenGL is right-handed: flip Z axis
                    double dzUnity = -dz;

                    // Find dominant axis and face
                    double absx = Math.Abs(dx), absy = Math.Abs(dy), absz = Math.Abs(dzUnity);
                    int faceIdx;
                    double u, v;

                    if (absx >= absy && absx >= absz)
                    {
                        if (dx > 0) { faceIdx = 0; u = -dzUnity / absx; v = dy / absx; }  // +X (right)
                        else { faceIdx = 1; u = dzUnity / absx; v = dy / absx; }          // -X (left)
                    }
                    else if (absy >= absx && absy >= absz)
                    {
                        if (dy > 0) { faceIdx = 2; u = dx / absy; v = -dzUnity / absy; }  // +Y (top)
                        else { faceIdx = 3; u = dx / absy; v = dzUnity / absy; }          // -Y (bottom)
                    }
                    else
                    {
                        if (dzUnity > 0) { faceIdx = 4; u = dx / absz; v = dy / absz; }   // +Z (Unity forward)
                        else { faceIdx = 5; u = -dx / absz; v = dy / absz; }              // -Z (Unity back)
                    }

                    // Convert UV from [-1,1] to pixel coords
                    int px = (int)(((u + 1) / 2) * (faceSize - 1));
                    int py = (int)(((1 - v) / 2) * (faceSize - 1));  // Flip V for image coords
                    px = Math.Clamp(px, 0, faceSize - 1);
                    py = Math.Clamp(py, 0, faceSize - 1);

                    equirect[x, y] = faces[faceIdx][px, py];
                }
            }

            // Cleanup face images
            foreach (var face in faces) face.Dispose();

            using var ms = new MemoryStream();
            equirect.SaveAsPng(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cubemap conversion failed");
            return null;
        }
    }

    private int DecodeTextureData(byte[] rawData, int width, int height, AssetRipper.SourceGenerated.Enums.TextureFormat format, byte[] rgbaData)
    {
        switch (format)
        {
            case AssetRipper.SourceGenerated.Enums.TextureFormat.RGBA32:
                return AssetRipper.TextureDecoder.Rgb.RgbConverter.Convert<
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte,
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    rawData, width, height, rgbaData);

            case AssetRipper.SourceGenerated.Enums.TextureFormat.RGB24:
                return AssetRipper.TextureDecoder.Rgb.RgbConverter.Convert<
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGB<byte>, byte,
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    rawData, width, height, rgbaData);

            case AssetRipper.SourceGenerated.Enums.TextureFormat.DXT1:
            case AssetRipper.SourceGenerated.Enums.TextureFormat.DXT1Crunched:
                return AssetRipper.TextureDecoder.Dxt.DxtDecoder.DecompressDXT1<
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    rawData, width, height, rgbaData);

            case AssetRipper.SourceGenerated.Enums.TextureFormat.DXT5:
            case AssetRipper.SourceGenerated.Enums.TextureFormat.DXT5Crunched:
                return AssetRipper.TextureDecoder.Dxt.DxtDecoder.DecompressDXT5<
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    rawData, width, height, rgbaData);

            case AssetRipper.SourceGenerated.Enums.TextureFormat.BC7:
                return AssetRipper.TextureDecoder.Bc.Bc7.Decompress<
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    rawData, width, height, rgbaData);

            default:
                _logger.LogWarning("Unsupported cubemap format: {TextureFormat}", format);
                return -1;
        }
    }

    private static List<string> CollectAssetFiles(string assetPath)
    {
        var files = new List<string>();

        // Primary asset file
        var resourcesPath = Path.Combine(assetPath, "resources.assets");
        if (File.Exists(resourcesPath))
            files.Add(resourcesPath);

        // Shared assets files (most textures are here)
        foreach (var file in Directory.GetFiles(assetPath, "sharedassets*.assets"))
        {
            files.Add(file);
        }

        // Level files
        foreach (var file in Directory.GetFiles(assetPath, "level*.assets"))
        {
            files.Add(file);
        }

        // Global game managers (IResourceManager is here)
        var globalPath = Path.Combine(assetPath, "globalgamemanagers");
        if (File.Exists(globalPath))
            files.Add(globalPath);

        // Global game managers .assets variant
        var globalAssetsPath = Path.Combine(assetPath, "globalgamemanagers.assets");
        if (File.Exists(globalAssetsPath))
            files.Add(globalAssetsPath);

        // Resource stream files
        foreach (var file in Directory.GetFiles(assetPath, "*.resS"))
        {
            files.Add(file);
        }

        // Resource files
        foreach (var file in Directory.GetFiles(assetPath, "*.resource"))
        {
            files.Add(file);
        }

        return files;
    }

    private static bool IsAllowedTexturePath(string relativePath)
    {
        string lowerPath = relativePath.ToLowerInvariant();

        // Icons folder - selective filtering
        if (lowerPath.StartsWith("assets/resources/icons/", StringComparison.Ordinal))
        {
            string afterIcons = lowerPath.Substring("assets/resources/icons/".Length);

            // Filter out root-level files (no slash = direct child of icons/)
            if (!afterIcons.Contains('/'))
                return false;

            // Filter out specific subfolders
            if (afterIcons.StartsWith("background_buildings/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("buffs/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("campaignicons/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("general_icons/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("guide_icons/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("observer/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("rank/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("rank_insara/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("rewards_icons/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("skins/", StringComparison.Ordinal))
                return false;

            return true;
        }

        // Objects folder - selective filtering
        if (lowerPath.StartsWith("assets/resources/objects/", StringComparison.Ordinal))
        {
            // Get the subfolder after objects/
            // Example: "assets/resources/objects/artifact/models/foo.png" -> "artifact/models/foo.png"
            string afterObjects = lowerPath.Substring("assets/resources/objects/".Length);

            // Only allow: artifact/, barracks/, interactive/, resource/
            bool isAllowedSubfolder =
                afterObjects.StartsWith("artifact/", StringComparison.Ordinal) ||
                afterObjects.StartsWith("barracks/", StringComparison.Ordinal) ||
                afterObjects.StartsWith("interactive/", StringComparison.Ordinal) ||
                afterObjects.StartsWith("resource/", StringComparison.Ordinal);

            if (!isAllowedSubfolder)
                return false;

            // Filter out models/ subfolder from artifact/, interactive/, resource/
            if (afterObjects.StartsWith("artifact/", StringComparison.Ordinal) ||
                afterObjects.StartsWith("interactive/", StringComparison.Ordinal) ||
                afterObjects.StartsWith("resource/", StringComparison.Ordinal))
            {
                // Check if path contains /models/
                if (afterObjects.Contains("/models/"))
                    return false;
            }

            // Filter out *_barracks/ patterns from barracks/ (castle_barracks/, dungeon_barracks/, etc.)
            if (afterObjects.StartsWith("barracks/", StringComparison.Ordinal))
            {
                string afterBarracks = afterObjects.Substring("barracks/".Length);
                // Check if the next segment ends with _barracks/
                int slashPos = afterBarracks.IndexOf('/');
                if (slashPos > 0)
                {
                    string subfolder = afterBarracks.Substring(0, slashPos);
                    if (subfolder.EndsWith("_barracks", StringComparison.Ordinal))
                        return false;
                }
            }

            return true;
        }

        // Environment textures - specific allowlist by name
        // These are UI/viewer textures needed for the unit viewer environment
        if (lowerPath.StartsWith("assets/texture2d/", StringComparison.Ordinal))
        {
            string fileName = Path.GetFileNameWithoutExtension(relativePath).ToLowerInvariant();
            return fileName == "unit_info_back" || fileName == "icon_lawspoint";
        }

        return false;
    }

    // Uses OriginalPath (set by IResourceManager) for PathID-based path resolution
    private static string ExtractTexturePath(ITexture2D texture)
    {
        var fileName = texture.OriginalName ?? texture.Name ?? $"UnknownTexture_{texture.PathID}";
        fileName = SanitizeFileName(fileName);

        // Step 1: Use OriginalPath if set by IResourceManager
        // This is the definitive path from Unity's resource system, mapped by PathID
        var originalPath = texture.OriginalPath;
        if (!string.IsNullOrEmpty(originalPath))
        {
            // OriginalPath is like "Assets/Resources/icons/units/hex_portraits/olgoi"
            // Get directory part and append sanitized filename
            int lastSlash = originalPath.LastIndexOf('/');
            if (lastSlash > 0)
            {
                string directory = originalPath.Substring(0, lastSlash);
                return $"{directory}/{fileName}";
            }
            else
            {
                return $"Assets/Resources/{fileName}";
            }
        }

        // Step 2: Fallback to GetBestDirectory() for textures not in Resources
        var fallbackDirectory = texture.GetBestDirectory();

        if (fallbackDirectory.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{fallbackDirectory}/{fileName}";
        }
        else
        {
            return $"Assets/{fallbackDirectory}/{fileName}";
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "UnknownTexture";

        // Replace invalid path characters
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            name = name.Replace(c, '_');
        }

        // Limit length
        if (name.Length > 200)
            name = name.Substring(0, 200);

        return name;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Only dispose GameBundle if we created it (not if provided externally)
        if (!_externalGameBundle)
        {
            _gameBundle?.Dispose();
        }

        _gameBundle = null;
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}

public class ExtractionStats
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
}
