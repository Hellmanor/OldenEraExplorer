#nullable enable
namespace AssetExtractor.Models;

/// <summary>
/// Used for UI elements, platform elements, and other non-unit/non-map-object prefabs.
/// </summary>
public class GameObjectData : PrefabData
{
    /// <summary>
    /// Pseudo-skeleton built from hierarchy nodes. GameObjects may have transform animations like map objects.
    /// </summary>
    public SkeletonData? Skeleton { get; set; }
    public List<AnimationData> Animations { get; set; } = new();

    public GameObjectData()
    {
        Type = PrefabType.GameObject;
    }
}
