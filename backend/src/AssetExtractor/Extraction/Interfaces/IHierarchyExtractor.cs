#nullable enable
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetExtractor.Models;

namespace AssetExtractor.Extraction.Interfaces;

/// <summary>
/// Interface for extracting hierarchy data from Unity assets.
/// </summary>
public interface IHierarchyExtractor
{
    /// <summary>
    /// Build hierarchy node from IGameObject using typed AssetRipper API.
    /// </summary>
    HierarchyNode? BuildHierarchyNode(IGameObject gameObject);

    /// <summary>
    /// Select wrapper and inner nodes from prefab hierarchy.
    /// </summary>
    (HierarchyNode? wrapper, HierarchyNode? inner) SelectWrapperAndInnerNodes(
        HierarchyNode prefabRoot,
        string unitName);

    /// <summary>
    /// Select wrapper and inner nodes for map objects (simpler than units).
    /// </summary>
    (HierarchyNode? wrapper, HierarchyNode? inner) SelectWrapperAndInnerNodesForMapObject(
        HierarchyNode root);
}
