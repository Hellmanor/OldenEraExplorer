#nullable enable
using AssetExtractor.Models;
using SharpGLTF.Scenes;

namespace AssetExtractor.Export.Interfaces;

/// <summary>
/// Interface for building glTF node hierarchies.
/// </summary>
public interface INodeHierarchyBuilder
{
    /// <summary>
    /// Build node hierarchy from UnitData.
    /// </summary>
    /// <param name="applyGltfForward">If true, applies 90° Y rotation for Unity→glTF forward conversion.</param>
    /// <param name="isometricExtra">If true, adds extra 45° for isometric map objects (total 135°).</param>
    Dictionary<string, NodeBuilder> BuildNodeHierarchy(
        SceneBuilder scene,
        UnitData unitData,
        HashSet<string> usedNodeNames,
        bool applyGltfForward = true,
        bool isometricExtra = false);

    /// <summary>
    /// Add skeleton bones to scene.
    /// </summary>
    void AddSkeleton(
        SceneBuilder scene,
        UnitData unitData,
        Dictionary<string, NodeBuilder> nodeBuilders,
        HashSet<string> usedNodeNames);
}
