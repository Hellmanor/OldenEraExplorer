#nullable enable
namespace AssetExtractor.Models;

/// <summary>
/// Map objects may have simple transform animations (e.g., rotating spirals).
/// </summary>
public class MapObjectData : PrefabData
{
    public string Category { get; set; } = string.Empty;
    public bool IsInteractive { get; set; }
    /// <summary>
    /// Pseudo-skeleton built from hierarchy nodes. Map objects don't have skinned meshes, but may have transform animations.
    /// </summary>
    public SkeletonData? Skeleton { get; set; }
    public List<AnimationData> Animations { get; set; } = new();

    public MapObjectData()
    {
        Type = PrefabType.MapObject;
    }
}
