namespace AssetExtractor.Models;

/// <summary>
/// Metadata for listing and discovering prefabs without loading full data.
/// </summary>
public class PrefabDescriptor
{
    public string Name { get; set; } = string.Empty;
    public PrefabType Type { get; set; }
    public string Category { get; set; } = string.Empty;
    public string ResourcePath { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    /// <summary>
    /// Used to uniquely identify the prefab when multiple prefabs have the same name.
    /// </summary>
    public long PathID { get; set; }
    /// <summary>
    /// Combined with PathID, provides unique identification across the entire bundle.
    /// </summary>
    public string AssetFile { get; set; } = string.Empty;
}
