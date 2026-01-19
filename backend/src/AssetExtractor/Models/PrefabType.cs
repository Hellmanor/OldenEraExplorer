namespace AssetExtractor.Models;

public enum PrefabType
{
    Unit,
    MapObject,
    Hero,
    /// <summary>
    /// May be part of multi-skeleton setups.
    /// </summary>
    Mount,
    Effect,
    GameObject
}
