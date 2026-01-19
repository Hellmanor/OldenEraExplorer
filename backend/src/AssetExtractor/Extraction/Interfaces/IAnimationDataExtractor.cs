using AssetExtractor.Models;

namespace AssetExtractor.Extraction.Interfaces;

/// <summary>
/// Interface for extracting animation data from Unity assets.
/// </summary>
public interface IAnimationDataExtractor
{
    /// <summary>
    /// Extract animations from an inner node using the Animator component.
    /// </summary>
    List<AnimationData> ExtractAnimations(
        HierarchyNode inner,
        SkeletonData skeleton,
        List<string> skinJointPaths);
}
