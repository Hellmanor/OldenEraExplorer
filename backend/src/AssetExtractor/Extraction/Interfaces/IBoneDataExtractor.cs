using AssetExtractor.Models;

namespace AssetExtractor.Extraction.Interfaces;

/// <summary>
/// Interface for extracting bone/skeleton data from Unity assets.
/// </summary>
public interface IBoneDataExtractor
{
    /// <summary>
    /// Extract skin joint paths from all SkinnedMeshRenderers under inner node (direct children only).
    /// </summary>
    List<string> ExtractSkinJointPaths(HierarchyNode inner, long animatorTransformPathId);

    /// <summary>
    /// Extract skin joint paths from all SkinnedMeshRenderers recursively under inner node.
    /// Used for map objects where SMRs may be deeply nested.
    /// </summary>
    List<string> ExtractSkinJointPathsRecursive(HierarchyNode inner, long animatorTransformPathId);

    /// <summary>
    /// Map bone indices from SMR's bone array to skeleton indices.
    /// </summary>
    int[] MapBoneIndicesToSkeleton(
        AssetRipper.SourceGenerated.Classes.ClassID_137.ISkinnedMeshRenderer smr,
        Dictionary<string, int> skeletonIndexByPath,
        long animatorTransformPathId);

    /// <summary>
    /// Build transform path relative to animator transform.
    /// </summary>
    string BuildTransformPath(
        AssetRipper.SourceGenerated.Classes.ClassID_4.ITransform transform,
        long animatorTransformPathId);

    /// <summary>
    /// Get the Transform component PathID for the inner/animator node.
    /// </summary>
    long GetAnimatorTransformPathId(HierarchyNode innerNode);
}
