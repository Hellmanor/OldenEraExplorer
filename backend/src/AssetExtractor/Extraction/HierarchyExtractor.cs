#nullable enable
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_95;
using AssetRipper.SourceGenerated.Extensions;
using AssetExtractor.Models;
using AssetExtractor.Extraction.Interfaces;
using AssetExtractor.Utilities;

namespace AssetExtractor.Extraction;

public class HierarchyExtractor : IHierarchyExtractor
{
    private readonly IGameObjectProvider _gameObjectProvider;

    public HierarchyExtractor(IGameObjectProvider gameObjectProvider)
    {
        _gameObjectProvider = gameObjectProvider;
    }

    public HierarchyNode? BuildHierarchyNode(IGameObject gameObject)
    {
        var node = new HierarchyNode
        {
            Name = gameObject.Name,
            PathID = gameObject.PathID,
            SourceFile = gameObject.Collection.Name,
            IsActive = gameObject.GetIsActive()
        };

        if (!gameObject.TryGetComponent<ITransform>(out var transform))
        {
            return node;
        }

        node.LocalTransform = new Models.Transform
        {
            Position = new Models.Vector3(
                transform.LocalPosition_C4.X,
                transform.LocalPosition_C4.Y,
                transform.LocalPosition_C4.Z
            ),
            Rotation = new Models.Quaternion(
                transform.LocalRotation_C4.X,
                transform.LocalRotation_C4.Y,
                transform.LocalRotation_C4.Z,
                transform.LocalRotation_C4.W
            ),
            Scale = new Models.Vector3(
                transform.LocalScale_C4.X,
                transform.LocalScale_C4.Y,
                transform.LocalScale_C4.Z
            )
        };

        foreach (var childTransform in transform.Children_C4P.WhereNotNull())
        {
            var childGO = childTransform.GameObject_C4P;
            if (childGO == null)
            {
                continue;
            }

            var childNode = BuildHierarchyNode(childGO);
            if (childNode != null)
            {
                childNode.Parent = node;
                node.Children.Add(childNode);
            }
        }

        return node;
    }

    public (HierarchyNode? wrapper, HierarchyNode? inner) SelectWrapperAndInnerNodes(
        HierarchyNode prefabRoot,
        string unitName)
    {
        foreach (var child in prefabRoot.Children)
        {
            if (IsWrapperNode(child))
            {
                var innerCandidates = new[] { child }.Concat(child.Children)
                    .Where(n => n.IsActive && HasAnimatorComponent(n) && !IsVFXNode(n.Name))
                    .ToList();

                if (innerCandidates.Count == 0)
                {
                    continue;
                }

                if (innerCandidates.Count == 1)
                {
                    return (child, innerCandidates[0]);
                }

                HierarchyNode? best = null;
                int bestScore = -1;

                foreach (var candidate in innerCandidates)
                {
                    int score = ScoreInnerCandidate(candidate, unitName);
                    if (score > bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }

                if (best != null)
                {
                    return (child, best);
                }
            }
        }

        var candidates = new List<HierarchyNode>();
        SearchForAnimator(prefabRoot, candidates);

        if (candidates.Count > 0)
        {
            HierarchyNode? foundInner = null;
            int bestScore = -1;

            foreach (var candidate in candidates)
            {
                int score = ScoreInnerCandidate(candidate, unitName);
                if (score > bestScore)
                {
                    foundInner = candidate;
                    bestScore = score;
                }
            }

            if (foundInner != null)
            {
                // Find the wrapper node by traversing up to find a scale_roll/wrapper node
                // This ensures consistent rotation handling across all prefab structures
                var wrapper = FindWrapperAncestor(foundInner, prefabRoot) ?? foundInner.Parent ?? prefabRoot;
                return (wrapper, foundInner);
            }
        }

        return (null, null);
    }

    // Map objects always use root for both wrapper and inner.
    // No scale stripping needed - there's no external JSON scale data (unlike units).
    public (HierarchyNode? wrapper, HierarchyNode? inner) SelectWrapperAndInnerNodesForMapObject(
        HierarchyNode root)
    {
        return (root, root);
    }

    // Find wrapper node by traversing up from inner node.
    // Stops at prefabRoot to avoid escaping the prefab hierarchy.
    private static HierarchyNode? FindWrapperAncestor(HierarchyNode inner, HierarchyNode prefabRoot)
    {
        var current = inner.Parent;
        while (current != null && current != prefabRoot)
        {
            if (IsWrapperNode(current))
            {
                return current;
            }
            current = current.Parent;
        }
        return null;
    }

    private void SearchForAnimator(HierarchyNode node, List<HierarchyNode> candidates)
    {
        // Skip inactive nodes
        if (!node.IsActive)
        {
            return;
        }

        if (HasAnimatorComponent(node) && !IsVFXNode(node.Name))
        {
            candidates.Add(node);
        }

        foreach (var child in node.Children)
        {
            SearchForAnimator(child, candidates);
        }
    }

    /// <summary>
    /// Score an inner node candidate for unit extraction.
    /// Higher scores indicate better matches for the requested unit name.
    ///
    /// Scoring weights:
    /// - 10000: Exact name match (highest confidence, e.g., "esquire" matches "esquire")
    /// - 5000: Prefix match (very likely correct variant, e.g., "esquire_2" for "esquire")
    /// - 1000: Contains match (possible but less certain)
    /// - -2000: Penalty for upgrade/alternate variants when searching for base unit
    ///          (prevents "esquire_upg" matching when "esquire" is requested)
    /// </summary>
    private static int ScoreInnerCandidate(HierarchyNode candidate, string unitName)
    {
        int score = 0;
        var candidateName = candidate.Name ?? "";
        var candidateLower = candidateName.ToLowerInvariant();
        var unitLower = unitName.ToLowerInvariant();

        // Exact name match: highest confidence
        if (candidateLower == unitLower)
        {
            score += 10000;
        }
        // Prefix match: very likely correct variant
        else if (candidateLower.StartsWith(unitLower))
        {
            score += 5000;
        }
        // Contains match: possible but less certain
        else if (candidateLower.Contains(unitLower))
        {
            score += 1000;
        }

        // Penalize upgrade/alternate variants when searching for base unit
        if (candidateName.Contains("_upg") || candidateName.Contains("_alt"))
        {
            if (!unitName.Contains("_upg") && !unitName.Contains("_alt"))
            {
                score -= 2000;
            }
        }

        return score;
    }

    private static bool IsWrapperNode(HierarchyNode node)
    {
        const float TOLERANCE = 0.0001f;
        // 0.707107f ≈ cos(45°) = sin(45°)
        // Detects wrapper nodes with 45° Y-axis rotation (isometric view correction).
        // Unity prefabs use these wrapper nodes to orient assets for the isometric camera.
        // Quaternion for 45° Y rotation: (0, ±0.707107, 0, ±0.707107)
        const float WRAPPER_Y_ROTATION = 0.707107f;

        var rot = node.LocalTransform.Rotation;

        if (Math.Abs(rot.X) < TOLERANCE &&
            Math.Abs(rot.Z) < TOLERANCE &&
            Math.Abs(Math.Abs(rot.Y) - WRAPPER_Y_ROTATION) < TOLERANCE &&
            Math.Abs(Math.Abs(rot.W) - WRAPPER_Y_ROTATION) < TOLERANCE)
        {
            return true;
        }

        if (node.Name?.Contains("scale_roll", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return false;
    }

    // Unity prefab-specific scale wrappers that should not be exported to GLB
    public static bool IsScaleWrapperNode(HierarchyNode node)
    {
        if (node.Name?.Contains("scale_roll", StringComparison.OrdinalIgnoreCase) == true ||
            node.Name?.Contains("scale_wrapper", StringComparison.OrdinalIgnoreCase) == true ||
            node.Name?.EndsWith("_roll", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return false;
    }

    private bool HasAnimatorComponent(HierarchyNode node)
    {
        var gameObject = _gameObjectProvider.FindGameObjectByPathId(node.SourceFile, node.PathID);
        if (gameObject == null)
        {
            return false;
        }

        return gameObject.TryGetComponent<IAnimator>(out _);
    }

    // VFX nodes are particle effects, not meshes - excluded from extraction
    //
    // Pattern categories:
    // - Prefixes: fx_, vfx_, global_, slash (effect containers)
    // - Infixes: fx_, _hit_, _receiver, _emitter, _impact_, _ability_, _wave (effect components)
    // - Suffixes: _vfx, _wave, _hit_root (effect markers)
    // - Exact matches: fx_empty, transform (placeholder nodes)
    public static bool IsVFXNode(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        name = name.Trim();
        var lower = name.ToLowerInvariant();

        // Prefix patterns (effect containers)
        if (lower.StartsWith("fx_") ||
            lower.StartsWith("vfx_") ||
            lower.StartsWith("global_") ||
            lower.StartsWith("slash"))
        {
            return true;
        }

        // Infix patterns (effect components)
        if (lower.Contains("fx_") ||
            lower.Contains("_hit_") ||
            lower.Contains("_receiver") ||
            lower.Contains("_emitter") ||
            lower.Contains("_impact_") ||
            lower.Contains("_ability_") ||
            lower.Contains("_wave"))
        {
            return true;
        }

        // Suffix patterns (effect markers)
        if (lower.EndsWith("_vfx") ||
            lower.EndsWith("_wave") ||
            lower.EndsWith("_hit_root"))
        {
            return true;
        }

        // Exact matches (placeholder nodes)
        if (lower == "fx_empty" || lower == "transform")
        {
            return true;
        }

        return false;
    }

    // Terrain blend meshes are disabled at runtime - excluded from extraction
    public static bool IsTerrainBlendNode(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var lower = name.Trim().ToLowerInvariant();

        // Ground_00, Ground_01, etc. - terrain blend meshes
        return lower.StartsWith("ground_") && char.IsDigit(lower[^1]);
    }
}
