#nullable enable
using AssetExtractor.Models;
using SharpGLTF.Scenes;

namespace AssetExtractor.Export.Interfaces;

/// <summary>
/// Interface for exporting animation data to glTF format.
/// </summary>
public interface IAnimationExporter
{
    /// <summary>
    /// Add animations to the glTF scene.
    /// </summary>
    void AddAnimations(
        SceneBuilder scene,
        List<AnimationData> animations,
        SkeletonData skeleton,
        Dictionary<string, NodeBuilder> nodeBuilders);
}
