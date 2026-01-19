using AssetRipper.SourceGenerated.Classes.ClassID_137;
using AssetRipper.SourceGenerated.Classes.ClassID_23;
using AssetExtractor.Models;

namespace AssetExtractor.Extraction.Interfaces;

/// <summary>
/// Interface for extracting mesh data from Unity assets.
/// </summary>
public interface IMeshDataExtractor
{
    /// <summary>
    /// Extract meshes from an inner node for unit prefabs.
    /// </summary>
    List<MeshData> ExtractMeshesFromInner(
        HierarchyNode inner,
        SkeletonData skeleton,
        UnitData unitData,
        long animatorTransformPathId);

    /// <summary>
    /// Extract meshes from a map object prefab.
    /// </summary>
    List<MeshData> ExtractMeshesFromMapObject(
        HierarchyNode inner,
        MapObjectData objectData);

    /// <summary>
    /// Extract meshes from a generic GameObject prefab.
    /// </summary>
    List<MeshData> ExtractMeshesFromGameObject(
        HierarchyNode node,
        GameObjectData objectData);
}
