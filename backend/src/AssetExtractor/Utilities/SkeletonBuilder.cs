#nullable enable
using AssetExtractor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Utilities;

/// <summary>
/// Handles building skeleton data from Unity hierarchy nodes.
/// Stateless utility class - all methods take inputs as parameters.
/// </summary>
public static class SkeletonBuilder
{
    /// <summary>
    /// Find skeleton roots from inner node using skin joint paths.
    /// Unity animation paths are relative to the Animator GameObject.
    /// Therefore, skeleton roots are the first segment of bone paths (direct children under the Animator).
    /// </summary>
    public static List<HierarchyNode> FindSkeletonRoots(
        HierarchyNode inner,
        List<string> skinJointPaths,
        ILogger? logger = null)
    {
        var orderedRootNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in skinJointPaths)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            var firstSlash = path.IndexOf('/');
            string rootName = firstSlash >= 0 ? path.Substring(0, firstSlash) : path;
            if (string.IsNullOrEmpty(rootName))
                continue;

            if (seen.Add(rootName))
            {
                orderedRootNames.Add(rootName);
            }
        }

        var roots = new List<HierarchyNode>();
        foreach (var rootName in orderedRootNames)
        {
            var matches = inner.Children
                .Where(c => string.Equals(c.Name, rootName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                (logger ?? NullLogger.Instance).LogWarning(
                    "Skeleton root candidate '{RootName}' not found as a direct child of '{InnerNodeName}'",
                    rootName,
                    inner.Name);
                continue;
            }

            // Prefer the match with children (bones), if multiple exist
            var best = matches
                .OrderByDescending(m => m.Children.Count)
                .First();

            roots.Add(best);
        }

        return roots;
    }

    /// <summary>
    /// Find skeleton root (child of inner named "Root") - legacy fallback
    /// </summary>
    public static HierarchyNode? FindSkeletonRoot(HierarchyNode inner)
    {
        foreach (var child in inner.Children)
        {
            if (string.Equals(child.Name, "Root", StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    /// <summary>
    /// Build skeleton data from a single skeleton root using SMR bone order, then add remaining hierarchy bones
    /// </summary>
    public static SkeletonData BuildSkeletonData(
        HierarchyNode skeletonRoot,
        List<string> skinJointPaths,
        Func<string, bool> isVFXNode,
        ILogger? logger = null)
    {
        return BuildSkeletonData(new List<HierarchyNode> { skeletonRoot }, skinJointPaths, isVFXNode, logger);
    }

    /// <summary>
    /// Build skeleton data from multiple skeleton roots (horse + rider + weapon, etc).
    /// Paths are relative to the Animator GameObject (Unity AnimationClip binding paths).
    /// </summary>
    public static SkeletonData BuildSkeletonData(
        List<HierarchyNode> skeletonRoots,
        List<string> skinJointPaths,
        Func<string, bool> isVFXNode,
        ILogger? logger = null)
    {
        if (skeletonRoots.Count == 0)
        {
            throw new ArgumentException("skeletonRoots cannot be empty", nameof(skeletonRoots));
        }

        var skeleton = new SkeletonData
        {
            RootBone = skeletonRoots[0]
        };

        // Build a lookup table from path to HierarchyNode for ALL roots
        var nodeByPath = new Dictionary<string, HierarchyNode>(StringComparer.Ordinal);
        foreach (var root in skeletonRoots)
        {
            BuildNodeLookup(root, "", nodeByPath, isVFXNode);
        }

        var log = logger ?? NullLogger.Instance;
        log.LogDebug(
            "Built node lookup with {NodeCount} nodes",
            nodeByPath.Count);
        log.LogDebug(
            "Skin joint paths count: {SkinJointPathCount}",
            skinJointPaths.Count);

        // Track which paths have been added as bones
        var addedPaths = new HashSet<string>(StringComparer.Ordinal);

        // STEP 0: Add skeleton root bones FIRST (ensures parent paths exist for e.g. Root/Hips)
        foreach (var root in skeletonRoots)
        {
            if (addedPaths.Contains(root.Name))
            {
                continue;
            }

            var rootBoneData = new BoneData
            {
                Name = root.Name,
                Path = root.Name,
                Index = skeleton.Bones.Count,
                ParentIndex = -1,
                LocalTransform = root.LocalTransform,
                WorldMatrix = Matrix4x4.Identity,
                InverseBindMatrix = Matrix4x4.Identity
            };

            skeleton.Bones.Add(rootBoneData);
            skeleton.RestPose[root.Name] = rootBoneData.WorldMatrix;
            addedPaths.Add(root.Name);
            log.LogDebug(
                "Added skeleton root bone: {RootBoneName}",
                root.Name);
        }

        // STEP 1: Build bones in SMR order (bones used by meshes for skinning)
        for (int i = 0; i < skinJointPaths.Count; i++)
        {
            string path = skinJointPaths[i];

            if (string.IsNullOrEmpty(path))
            {
                log.LogWarning(
                    "Skin joint {JointIndex} has empty path, skipping",
                    i);
                continue;
            }

            if (addedPaths.Contains(path))
            {
                continue; // Deduplicate paths (Unity bones can be shared across meshes)
            }

            if (!nodeByPath.TryGetValue(path, out var node))
            {
                log.LogWarning(
                    "Skin joint path '{JointPath}' not found in hierarchy",
                    path);
                continue;
            }

            // Find parent index by looking up parent path (may be fixed later in NormalizeSkeletonParentIndices)
            int parentIndex = -1;
            string parentPath = GetParentPath(path);
            if (!string.IsNullOrEmpty(parentPath))
            {
                parentIndex = skeleton.Bones.FindIndex(b => b.Path == parentPath);
            }

            var boneData = new BoneData
            {
                Name = node.Name,
                Path = path,
                Index = skeleton.Bones.Count,
                ParentIndex = parentIndex,
                LocalTransform = node.LocalTransform,
                WorldMatrix = Matrix4x4.Identity,
                InverseBindMatrix = Matrix4x4.Identity
            };

            skeleton.Bones.Add(boneData);
            skeleton.RestPose[path] = boneData.WorldMatrix;
            addedPaths.Add(path);
        }

        log.LogDebug(
            "Added {BoneCount} bones from SMR in order",
            skeleton.Bones.Count);

        // STEP 2: Add remaining bones from hierarchy (leaf bones, intermediate bones not in SMR)
        foreach (var root in skeletonRoots)
        {
            AddRemainingBones(root, "", skeleton, nodeByPath, addedPaths, isVFXNode, logger);
        }

        // Ensure ParentIndex values reflect the actual hierarchy (SMR bone order is not guaranteed to be topologically sorted)
        NormalizeSkeletonParentIndices(skeleton, logger);

        return skeleton;
    }

    /// <summary>
    /// Normalize skeleton ParentIndex and Index based on bone.Path.
    /// SMR bone order is important for skinning, but parent links must match the transform hierarchy.
    /// </summary>
    public static void NormalizeSkeletonParentIndices(SkeletonData skeleton, ILogger? logger = null)
    {
        var indexByPath = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            var bone = skeleton.Bones[i];
            bone.Index = i;
            indexByPath[bone.Path] = i;
        }

        int missingParents = 0;

        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            var bone = skeleton.Bones[i];
            string parentPath = GetParentPath(bone.Path);

            if (string.IsNullOrEmpty(parentPath))
            {
                bone.ParentIndex = -1;
                continue;
            }

            if (indexByPath.TryGetValue(parentPath, out int parentIndex))
            {
                bone.ParentIndex = parentIndex;
            }
            else
            {
                bone.ParentIndex = -1;
                missingParents++;
                if (missingParents <= 5)
                {
                    (logger ?? NullLogger.Instance).LogWarning(
                        "Missing parent '{ParentPath}' for bone '{BonePath}'",
                        parentPath,
                        bone.Path);
                }
            }
        }

        if (missingParents > 0)
        {
            (logger ?? NullLogger.Instance).LogWarning(
                "{MissingParentCount} bones have missing parents after normalization (armature may be invalid)",
                missingParents);
        }
    }

    /// <summary>
    /// Recursively add bones from hierarchy that aren't in the SMR bone list
    /// </summary>
    public static void AddRemainingBones(
        HierarchyNode node,
        string parentPath,
        SkeletonData skeleton,
        Dictionary<string, HierarchyNode> nodeByPath,
        HashSet<string> addedPaths,
        Func<string, bool> isVFXNode,
        ILogger? logger = null)
    {
        string path = string.IsNullOrEmpty(parentPath) ? node.Name : $"{parentPath}/{node.Name}";

        // If this bone hasn't been added yet, add it
        if (!addedPaths.Contains(path))
        {
            // Find parent index
            int parentIndex = -1;
            if (!string.IsNullOrEmpty(parentPath))
            {
                parentIndex = skeleton.Bones.FindIndex(b => b.Path == parentPath);
            }

            var boneData = new BoneData
            {
                Name = node.Name,
                Path = path,
                Index = skeleton.Bones.Count,
                ParentIndex = parentIndex,
                LocalTransform = node.LocalTransform,
                WorldMatrix = Matrix4x4.Identity,
                InverseBindMatrix = Matrix4x4.Identity
            };

            skeleton.Bones.Add(boneData);
            skeleton.RestPose[path] = boneData.WorldMatrix;
            addedPaths.Add(path);

            (logger ?? NullLogger.Instance).LogDebug(
                "Added non-SMR bone: {BonePath}",
                path);
        }

        // Recurse to children (excluding VFX nodes)
        foreach (var child in node.Children)
        {
            if (!isVFXNode(child.Name))
            {
                AddRemainingBones(child, path, skeleton, nodeByPath, addedPaths, isVFXNode, logger);
            }
        }
    }

    /// <summary>
    /// Build lookup table from path to HierarchyNode
    /// </summary>
    private static void BuildNodeLookup(HierarchyNode node, string parentPath, Dictionary<string, HierarchyNode> lookup, Func<string, bool> isVFXNode)
    {
        string path = string.IsNullOrEmpty(parentPath) ? node.Name : $"{parentPath}/{node.Name}";
        lookup[path] = node;

        foreach (var child in node.Children)
        {
            if (!isVFXNode(child.Name))
            {
                BuildNodeLookup(child, path, lookup, isVFXNode);
            }
        }
    }

    /// <summary>
    /// Get parent path from a full path
    /// </summary>
    private static string GetParentPath(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0)
            return "";
        return path.Substring(0, lastSlash);
    }
}
