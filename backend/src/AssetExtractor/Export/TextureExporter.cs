#nullable enable
using AssetRipper.Export.Modules.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_28;  // ITexture2D
using AssetExtractor.Models;
using AssetExtractor.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Export;

/// <summary>
/// Exports Unity Texture2D to PNG using AssetRipper's TextureConverter.
/// Thread-safe: locks around GetImageData (shared file stream reads not thread-safe).
/// </summary>
public class TextureExporter
{
    private readonly ILogger<TextureExporter> _logger;
    private readonly string _outputPath;
    private readonly ManifestManager _manifestService;

    // GetImageData reads shared file streams (not thread-safe). Lock serializes reads, parallelizes encoding/disk I/O.
    internal static readonly object TextureConversionLock = new();

    public TextureExporter(
        string outputPath,
        ManifestManager manifestService,
        ILogger<TextureExporter>? logger = null)
    {
        _logger = logger ?? NullLogger<TextureExporter>.Instance;
        _outputPath = outputPath;
        _manifestService = manifestService;
    }

    private bool ValidateTextureData(TextureData? textureData)
    {
        if (textureData == null || textureData.ImageData == null || textureData.ImageData.Length == 0)
        {
            _logger.LogWarning("Cannot export texture: no image data");
            return false;
        }
        return true;
    }

    public string? ExportTexture(TextureData textureData, string relativePath, string version)
    {
        if (!ValidateTextureData(textureData))
            return null;

        try
        {
            var versionOutputPath = _manifestService.GetVersionOutputPath(version);
            var fullPath = Path.Combine(versionOutputPath, relativePath + ".png");

            WriteTextureFile(fullPath, textureData.ImageData);
            _logger.LogInformation("Exported texture: {FullPath}", fullPath);

            var hash = ComputeHash(textureData.ImageData);
            _manifestService.AddOrUpdateVariant(
                relativePath,
                version,
                hash,
                textureData.ImageData.Length,
                "Texture2D",
                ".png");

            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export texture '{TextureName}'", textureData.Name);
            return null;
        }
    }

    private static string ComputeHash(byte[] data)
    {
        var hash = System.IO.Hashing.XxHash64.HashToUInt64(data);
        return hash.ToString("x16");
    }

    public string? ExportTexture(ITexture2D texture, string relativePath, string version)
    {
        using (_logger.BeginScope("Texture: {TexturePath}", relativePath))
        {
            if (texture == null)
            {
                _logger.LogWarning("Cannot export texture: null reference");
                return null;
            }

            try
            {
                var name = texture.Name ?? "UnknownTexture";
                int width = texture.Width_C28;
                int height = texture.Height_C28;

                _logger.LogInformation("Exporting texture: {TextureName} ({Width}x{Height})", name, width, height);

                byte[] pngData;

                // Lock covers GetImageData + PNG encoding. Disk I/O outside lock (parallelism).
                lock (TextureConversionLock)
                {
                    // TextureConverter handles all formats, applies FlipY for correct orientation
                    if (!TextureConverter.TryConvertToBitmap(texture, out var bitmap))
                    {
                        _logger.LogWarning("Failed to convert texture '{TextureName}' to bitmap", name);
                        return null;
                    }

                    using var ms = new MemoryStream();
                    bitmap.SaveAsPng(ms);
                    pngData = ms.ToArray();
                }
                var textureData = new TextureData
                {
                    Name = name,
                    Width = width,
                    Height = height,
                    ImageData = pngData,
                    Format = "PNG"
                };

                return ExportTexture(textureData, relativePath, version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export texture");
                return null;
            }
        }
    }

    private void WriteTextureFile(string fullPath, byte[] data)
    {
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, data);
    }

}
