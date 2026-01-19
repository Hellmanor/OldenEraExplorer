#nullable enable
using System.Collections.Concurrent;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetExtractor.Models;
using AssetExtractor.Export;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

// Thread-safety: AssetRipper's GetImageData() reads from shared streams (NOT thread-safe).
// Global lock ensures only one thread reads at a time.
// Lazy<T> guarantees each texture loads exactly once.
// TextureData is immutable after load - safe to share.
public class ThreadSafeTextureCache
{
    private readonly ILogger<ThreadSafeTextureCache> _logger;

    // Lazy<T> ensures thread-safe init - only one thread executes factory per PathID
    private readonly ConcurrentDictionary<long, Lazy<TextureData?>> _cache = new();

    public ThreadSafeTextureCache(ILogger<ThreadSafeTextureCache>? logger = null)
    {
        _logger = logger ?? NullLogger<ThreadSafeTextureCache>.Instance;
    }

    // Thread-safe: concurrent calls for same/different textures are safe
    public TextureData? GetOrLoad(ITexture2D texture)
    {
        // GetOrAdd is atomic - only one Lazy<T> instance is created per key
        var lazy = _cache.GetOrAdd(texture.PathID, _ => new Lazy<TextureData?>(() =>
        {
            // This factory runs under Lazy<T>'s internal lock
            // Only one thread will execute this code for each PathID
            return LoadTextureWithLock(texture);
        }));

        // .Value blocks if another thread is currently loading this texture
        return lazy.Value;
    }

    public TextureData? TryGetCached(long pathId)
    {
        if (_cache.TryGetValue(pathId, out var lazy) && lazy.IsValueCreated)
        {
            return lazy.Value;
        }
        return null;
    }

    public bool IsCached(long pathId)
    {
        return _cache.TryGetValue(pathId, out var lazy) && lazy.IsValueCreated;
    }

    public IEnumerable<TextureData> GetAllCached()
    {
        foreach (var kvp in _cache)
        {
            if (kvp.Value.IsValueCreated && kvp.Value.Value != null)
            {
                yield return kvp.Value.Value;
            }
        }
    }

    public void Clear()
    {
        _cache.Clear();
    }

    public int Count => _cache.Count(kvp => kvp.Value.IsValueCreated && kvp.Value.Value != null);

    // CRITICAL: Global lock around stream reads - AssetRipper's GetImageData() uses non-atomic stream operations
    // (Stream.Position + ReadExactly) which corrupt under concurrent access
    private TextureData? LoadTextureWithLock(ITexture2D texture)
    {
        var textureData = new TextureData
        {
            Name = texture.Name ?? "UnknownTexture",
            Width = texture.Width_C28,
            Height = texture.Height_C28
        };

        _logger.LogInformation(
            "Extracting texture: {TextureName} ({Width}x{Height})",
            textureData.Name,
            textureData.Width,
            textureData.Height);

        try
        {
            lock (TextureExporter.TextureConversionLock)
            {
                if (!TextureConverter.TryConvertToBitmap(texture, out var bitmap))
                {
                    _logger.LogWarning(
                        "Failed to convert texture '{TextureName}' to bitmap",
                        textureData.Name);
                    return null;
                }

                // AssetRipper applies FlipY for PNG display - undo for GLB embedding
                bitmap.FlipY();

                // Save as PNG (this part is thread-safe, could be outside lock if needed)
                using var ms = new MemoryStream();
                bitmap.SaveAsPng(ms);
                textureData.ImageData = ms.ToArray();
                textureData.Format = "PNG";
            }

            _logger.LogInformation(
                "Converted texture to PNG: {PngSize} bytes",
                textureData.ImageData.Length);
            return textureData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to extract texture '{TextureName}'",
                textureData.Name);
            return null;
        }
    }
}
