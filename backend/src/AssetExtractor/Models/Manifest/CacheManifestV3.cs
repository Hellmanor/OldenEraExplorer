#nullable enable
using System.Text.Json.Serialization;

namespace AssetExtractor.Models.Manifest;

/// <summary>
/// Cache Manifest V3 - Root manifest for tracking all extracted assets across versions.
/// Supports PNG textures and GLB models with deduplication and version management.
/// </summary>
public class CacheManifestV3
{
    /// <summary>
    /// Manifest version (always 3 for this schema).
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 3;

    /// <summary>
    /// ISO 8601 timestamp of last promotion run (null if never promoted).
    /// </summary>
    [JsonPropertyName("lastPromotionRun")]
    public string? LastPromotionRun { get; set; }

    /// <summary>
    /// Information about each extracted game version.
    /// Key: Normalized version string (e.g., "0.45.02-cb").
    /// </summary>
    [JsonPropertyName("builds")]
    public Dictionary<string, BuildInfo> Builds { get; set; } = new();

    /// <summary>
    /// All extracted assets (textures, models, etc.).
    /// Key: Relative path without extension (e.g., "Assets/Texture2D/icon_unit").
    /// </summary>
    [JsonPropertyName("assets")]
    public Dictionary<string, AssetInfoV3> Assets { get; set; } = new();
}

/// <summary>
/// Information about an extracted game version/build.
/// </summary>
public class BuildInfo
{
    /// <summary>
    /// Full path to game root directory (e.g., "/path/to/HeroesOE_Data").
    /// </summary>
    [JsonPropertyName("gameRootPath")]
    public string GameRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Normalized version string (e.g., "0.45.02-cb").
    /// </summary>
    [JsonPropertyName("gameVersion")]
    public string GameVersion { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 timestamp of when this version was extracted.
    /// </summary>
    [JsonPropertyName("extractedAt")]
    public string ExtractedAt { get; set; } = string.Empty;

    /// <summary>
    /// XXHash64 combined hash of main asset files (for change detection).
    /// </summary>
    [JsonPropertyName("assetsHash")]
    public string AssetsHash { get; set; } = string.Empty;
}

/// <summary>
/// Information about an extracted asset (texture or model).
/// </summary>
public class AssetInfoV3
{
    /// <summary>
    /// Asset type ("Texture2D" or "Model").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Status: "shared" | "partial" | "build-specific".
    /// - shared: Asset identical in ALL versions
    /// - partial: Asset identical in 2+ versions (but not all)
    /// - build-specific: All versions have different assets
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "build-specific";

    /// <summary>
    /// XXHash64 of the shared asset (null for build-specific).
    /// For "partial" status, this is the hash of the most common variant.
    /// </summary>
    [JsonPropertyName("sharedHash")]
    public string? SharedHash { get; set; }

    /// <summary>
    /// File size in bytes of shared asset (null for build-specific).
    /// </summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }

    /// <summary>
    /// List of versions using the shared asset (null for build-specific).
    /// For "shared" status, contains ALL versions.
    /// For "partial" status, contains versions using the shared hash.
    /// </summary>
    [JsonPropertyName("versions")]
    public List<string>? Versions { get; set; }

    /// <summary>
    /// Version-specific variants (null for fully shared).
    /// For "build-specific" status, contains ALL versions.
    /// For "partial" status, contains only differing versions.
    /// Key: Version string (e.g., "0.46.10-demo").
    /// </summary>
    [JsonPropertyName("variants")]
    public Dictionary<string, AssetVariantV3>? Variants { get; set; }

    /// <summary>
    /// File extension for the asset (e.g., ".png", ".glb").
    /// Used for file path generation.
    /// </summary>
    [JsonPropertyName("extension")]
    public string Extension { get; set; } = string.Empty;
}

/// <summary>
/// Information about a version-specific asset variant.
/// </summary>
public class AssetVariantV3
{
    /// <summary>
    /// XXHash64 of this variant.
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }
}
