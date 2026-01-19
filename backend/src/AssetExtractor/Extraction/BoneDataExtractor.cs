#nullable enable
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_137;
using AssetRipper.SourceGenerated.Extensions;
using AssetExtractor.Models;
using AssetExtractor.Extraction.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

public class BoneDataExtractor : IBoneDataExtractor
{
    private readonly ILogger<BoneDataExtractor> _logger;
    private readonly IGameObjectProvider _gameObjectProvider;

    public BoneDataExtractor(
        IGameObjectProvider gameObjectProvider,
        ILogger<BoneDataExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<BoneDataExtractor>.Instance;
        _gameObjectProvider = gameObjectProvider;
    }

    public List<string> ExtractSkinJointPaths(HierarchyNode inner, long animatorTransformPathId)
    {
        var uniqueBonePaths = new HashSet<string>();
        var orderedBonePaths = new List<string>();

        foreach (var child in inner.Children)
        {
            if (string.Equals(child.Name, "Root", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var gameObject = _gameObjectProvider.FindGameObjectByPathId(child.SourceFile, child.PathID);
            if (gameObject == null)
            {
                continue;
            }

            if (!gameObject.TryGetComponent<ISkinnedMeshRenderer>(out var smr))
            {
                continue;
            }

            _logger.LogInformation("Collecting bones from mesh '{MeshName}'", child.Name);

            foreach (var bonePPtr in smr.Bones)
            {
                var boneTransform = bonePPtr.TryGetAsset(smr.Collection);
                if (boneTransform is not ITransform transform)
                {
                    continue;
                }

                string path = BuildTransformPath(transform, animatorTransformPathId);

                if (!uniqueBonePaths.Contains(path))
                {
                    uniqueBonePaths.Add(path);
                    orderedBonePaths.Add(path);
                }
            }
        }

        _logger.LogInformation(
            "Collected {BoneCount} unique bones from all meshes",
            orderedBonePaths.Count);
        return orderedBonePaths;
    }

    // Recursive variant for map objects where SkinnedMeshRenderers are deeply nested
    public List<string> ExtractSkinJointPathsRecursive(HierarchyNode inner, long animatorTransformPathId)
    {
        var uniqueBonePaths = new HashSet<string>();
        var orderedBonePaths = new List<string>();

        CollectSkinJointPathsRecursive(inner, animatorTransformPathId, uniqueBonePaths, orderedBonePaths);

        _logger.LogInformation(
            "Collected {BoneCount} unique bones from all meshes (recursive)",
            orderedBonePaths.Count);
        return orderedBonePaths;
    }

    private void CollectSkinJointPathsRecursive(
        HierarchyNode node,
        long animatorTransformPathId,
        HashSet<string> uniqueBonePaths,
        List<string> orderedBonePaths)
    {
        var gameObject = _gameObjectProvider.FindGameObjectByPathId(node.SourceFile, node.PathID);
        if (gameObject != null && gameObject.TryGetComponent<ISkinnedMeshRenderer>(out var smr))
        {
            _logger.LogInformation(
                "Collecting bones from mesh '{MeshName}' (recursive)",
                node.Name);

            foreach (var bonePPtr in smr.Bones)
            {
                var boneTransform = bonePPtr.TryGetAsset(smr.Collection);
                if (boneTransform is not ITransform transform)
                {
                    continue;
                }

                string path = BuildTransformPath(transform, animatorTransformPathId);

                if (uniqueBonePaths.Add(path))
                {
                    orderedBonePaths.Add(path);
                }
            }
        }

        // Recurse to children
        foreach (var child in node.Children)
        {
            CollectSkinJointPathsRecursive(child, animatorTransformPathId, uniqueBonePaths, orderedBonePaths);
        }
    }

    public int[] MapBoneIndicesToSkeleton(
        ISkinnedMeshRenderer smr,
        Dictionary<string, int> skeletonIndexByPath,
        long animatorTransformPathId)
    {
        var boneIndices = new List<int>();

        foreach (var bonePPtr in smr.Bones)
        {
            var boneAsset = bonePPtr.TryGetAsset(smr.Collection);
            if (boneAsset is not ITransform boneTransform)
            {
                boneIndices.Add(-1);
                continue;
            }

            string bonePath = BuildTransformPath(boneTransform, animatorTransformPathId);
            var boneName = boneTransform.GameObject_C4P?.Name ?? "";

            if (skeletonIndexByPath.TryGetValue(bonePath, out int skelIndex))
            {
                boneIndices.Add(skelIndex);
            }
            else
            {
                int nameMatch = -1;
                foreach (var kvp in skeletonIndexByPath)
                {
                    if (kvp.Key.EndsWith("/" + boneName) || kvp.Key == boneName)
                    {
                        nameMatch = kvp.Value;
                        break;
                    }
                }

                if (nameMatch >= 0)
                {
                    boneIndices.Add(nameMatch);
                }
                else
                {
                    _logger.LogWarning(
                        "Bone '{BoneName}' not found in skeleton (path: {BonePath})",
                        boneName,
                        bonePath);
                    boneIndices.Add(-1);
                }
            }
        }

        return boneIndices.ToArray();
    }

    public string BuildTransformPath(ITransform transform, long animatorTransformPathId)
    {
        var pathParts = new List<string>();
        ITransform? current = transform;

        while (current != null)
        {
            var gameObject = current.GameObject_C4P;
            var name = gameObject?.Name ?? "Unnamed";
            pathParts.Insert(0, name);

            var parent = current.Father_C4P;

            if (parent != null && parent.PathID == animatorTransformPathId)
            {
                break;
            }

            current = parent;
        }

        return string.Join("/", pathParts);
    }

    public long GetAnimatorTransformPathId(HierarchyNode innerNode)
    {
        var gameObject = _gameObjectProvider.FindGameObjectByPathId(innerNode.SourceFile, innerNode.PathID);
        if (gameObject == null)
        {
            return 0;
        }

        if (gameObject.TryGetComponent<ITransform>(out var transform))
        {
            return transform.PathID;
        }

        return 0;
    }
}
