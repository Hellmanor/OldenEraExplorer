#nullable enable
using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_23;
using AssetRipper.SourceGenerated.Classes.ClassID_95;
using AssetRipper.SourceGenerated.Classes.ClassID_137;
using AssetRipper.SourceGenerated.Extensions;
using AssetExtractor.Models;
using AssetExtractor.Extraction.Interfaces;
using AssetExtractor.Utilities;
using AssetExtractor.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

public class AssetExtractor : IDisposable, IGameObjectProvider
{
    private readonly ILogger<AssetExtractor> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly AssetLoader _assetLoader;
    private readonly MaterialExtractor _materialExtractor;

    private readonly IHierarchyExtractor _hierarchyExtractor;
    private readonly IBoneDataExtractor _boneDataExtractor;
    private readonly IMeshDataExtractor _meshDataExtractor;
    private readonly IAnimationDataExtractor _animationDataExtractor;

    private bool _disposed;
    private const float POSE_TIME_EPSILON = 0.0001f;

    public GameBundle GameBundle => _assetLoader.GameBundle;

    public AssetLoader Loader => _assetLoader;
    public AssetExtractor(string assetPath, ILogger<AssetExtractor>? logger = null, ILoggerFactory? loggerFactory = null)
    {
        _logger = logger ?? NullLogger<AssetExtractor>.Instance;
        _loggerFactory = loggerFactory;
        _materialExtractor = new MaterialExtractor(_loggerFactory?.CreateLogger<MaterialExtractor>());

        _logger.LogInformation("Initializing AssetExtractor");

        _assetLoader = new AssetLoader(assetPath, _loggerFactory?.CreateLogger<AssetLoader>());

        _hierarchyExtractor = new HierarchyExtractor(this);
        _boneDataExtractor = new BoneDataExtractor(this, _loggerFactory?.CreateLogger<BoneDataExtractor>());
        _meshDataExtractor = new MeshDataExtractor(this, _boneDataExtractor, _materialExtractor, _loggerFactory?.CreateLogger<MeshDataExtractor>());
        _animationDataExtractor = new AnimationDataExtractor(this, _assetLoader.GameBundle, _loggerFactory?.CreateLogger<AnimationDataExtractor>());
    }

    #region AssetLoader Delegation

    public List<string> ListResourcePaths(string? pattern = null) => _assetLoader.ListResourcePaths(pattern);

    public IGameObject? FindPrefabByNameWithResourcePaths(string prefabName, string? pathPrefix = null)
        => _assetLoader.FindPrefabByNameWithResourcePaths(prefabName, pathPrefix);

    public IGameObject? FindPrefabByResourcePath(string resourcePath)
        => _assetLoader.FindPrefabByResourcePath(resourcePath);

    public IGameObject? FindGameObjectByPathId(string sourceFile, long pathId)
        => _assetLoader.FindGameObjectByPathId(sourceFile, pathId);

    public IGameObject? FindPrefabByName(string prefabName)
        => _assetLoader.FindPrefabByName(prefabName);

    public List<string> ListAllPrefabNames() => _assetLoader.ListAllPrefabNames();

    public void DebugPrefabHierarchy(string prefabName) => _assetLoader.DebugPrefabHierarchy(prefabName);

    public void AnalyzeAssetStructure(string searchTerm) => _assetLoader.AnalyzeAssetStructure(searchTerm);

    #endregion

    #region Unit Extraction

    public UnitData ExtractUnit(string unitName)
    {
        _logger.LogInformation("Extracting unit: {UnitName}", unitName);

        var unitData = new UnitData { Name = unitName };

        // First try resource path lookup (future-proof for duplicates)
        var prefabRoot = FindPrefabByNameWithResourcePaths(unitName, "units/");
        if (prefabRoot != null)
        {
            _logger.LogInformation("Found unit by resource path (PathID: {PathId})", prefabRoot.PathID);
        }
        else
        {
            // Fall back to name-based lookup
            prefabRoot = FindPrefabByName(unitName);
            if (prefabRoot != null)
            {
                _logger.LogInformation("Found unit by name (PathID: {PathId})", prefabRoot.PathID);
            }
        }

        if (prefabRoot == null)
        {
            throw new Exception($"Prefab not found for unit '{unitName}'.");
        }

        _logger.LogInformation("Found prefab root: {PrefabRootName}", prefabRoot.Name);

        var rootNode = _hierarchyExtractor.BuildHierarchyNode(prefabRoot);
        if (rootNode == null)
        {
            throw new Exception($"Failed to build hierarchy for unit: {unitName}");
        }

        unitData.RootNode = rootNode;
        _logger.LogInformation("Built hierarchy with root: {RootName}", rootNode.Name);

        var (wrapperNode, innerNode) = _hierarchyExtractor.SelectWrapperAndInnerNodes(rootNode, unitName);
        if (wrapperNode == null || innerNode == null)
        {
            throw new Exception($"Wrapper/inner selection failed for unit: {unitName}");
        }

        _logger.LogInformation("Found wrapper node: {WrapperName}, inner node: {InnerName}", wrapperNode.Name, innerNode.Name);
        unitData.WrapperNode = wrapperNode;
        unitData.InnerNode = innerNode;

        var boneExtractor = (BoneDataExtractor)_boneDataExtractor;
        var animatorTransformPathId = boneExtractor.GetAnimatorTransformPathId(innerNode);

        _logger.LogInformation("Extracting skin joint paths");
        var skinJointPaths = _boneDataExtractor.ExtractSkinJointPaths(innerNode, animatorTransformPathId);
        _logger.LogInformation("Found {SkinJointCount} skin joints", skinJointPaths.Count);

        _logger.LogInformation("Searching for skeleton root(s)");
        var skeletonRoots = SkeletonBuilder.FindSkeletonRoots(innerNode, skinJointPaths, _logger);
        if (skeletonRoots.Count == 0)
        {
            var legacyRoot = SkeletonBuilder.FindSkeletonRoot(innerNode);
            if (legacyRoot != null)
            {
                skeletonRoots.Add(legacyRoot);
            }
        }

        if (skeletonRoots.Count == 0)
        {
            throw new Exception($"Skeleton root not found for unit: {unitName}");
        }

        _logger.LogInformation("Found skeleton root(s): {SkeletonRoots}", string.Join(", ", skeletonRoots.Select(r => r.Name)));

        _logger.LogInformation("Building bone hierarchy");
        unitData.Skeleton = SkeletonBuilder.BuildSkeletonData(skeletonRoots, skinJointPaths, HierarchyExtractor.IsVFXNode, _logger);
        _logger.LogInformation("Total bones: {BoneCount}", unitData.Skeleton.Bones.Count);

        _logger.LogInformation("Extracting meshes");
        unitData.Meshes = _meshDataExtractor.ExtractMeshesFromInner(innerNode, unitData.Skeleton, unitData, animatorTransformPathId);
        _logger.LogInformation("Total meshes: {MeshCount}", unitData.Meshes.Count);

        _logger.LogInformation("Extracting animations");
        unitData.Animations = _animationDataExtractor.ExtractAnimations(innerNode, unitData.Skeleton, skinJointPaths);
        _logger.LogInformation("Total animations: {AnimationCount}", unitData.Animations.Count);

        TryApplyIdlePoseAsRestPose(unitData);

        _logger.LogInformation("Unit extraction complete");

        return unitData;
    }

    #endregion

    #region MapObject Extraction

    public MapObjectData ExtractMapObject(string objectName)
    {
        _logger.LogInformation("Extracting map object: {ObjectName}", objectName);

        var objectData = new MapObjectData { Name = objectName };

        string prefabName = objectName;
        if (objectName.Contains('/'))
        {
            var parts = objectName.Split('/');
            objectData.Category = parts[0];
            prefabName = parts[^1];
        }

        // First try to find by resource path (handles multiple prefabs with same name)
        var prefabRoot = FindPrefabByResourcePath($"objects/{objectName}");
        if (prefabRoot != null)
        {
            _logger.LogInformation("Found prefab by resource path: objects/{ObjectName} (PathID: {PathId})", objectName, prefabRoot.PathID);
        }
        else
        {
            // Fall back to name-based lookup for backward compatibility
            prefabRoot = FindPrefabByName(prefabName);
            if (prefabRoot != null)
            {
                _logger.LogInformation("Found prefab by name: {PrefabName} (PathID: {PathId})", prefabName, prefabRoot.PathID);
            }
        }

        if (prefabRoot == null)
        {
            throw new Exception($"Prefab not found for map object '{objectName}'");
        }

        _logger.LogInformation("Found prefab root: {PrefabRootName}", prefabRoot.Name);

        var rootNode = _hierarchyExtractor.BuildHierarchyNode(prefabRoot);
        if (rootNode == null)
        {
            throw new Exception($"Failed to build hierarchy for map object: {objectName}");
        }

        objectData.RootNode = rootNode;

        var (wrapperNode, innerNode) = _hierarchyExtractor.SelectWrapperAndInnerNodesForMapObject(rootNode);
        objectData.WrapperNode = wrapperNode ?? rootNode;
        objectData.InnerNode = innerNode ?? rootNode;

        // Get animator transform PathID for consistent bone path building
        var animatorTransformPathId = _boneDataExtractor.GetAnimatorTransformPathId(objectData.InnerNode);

        // Check for skinned meshes recursively and extract their bone paths
        var skinJointPaths = _boneDataExtractor.ExtractSkinJointPathsRecursive(objectData.InnerNode, animatorTransformPathId);

        List<HierarchyNode> skeletonRoots;
        if (skinJointPaths.Count > 0)
        {
            skeletonRoots = SkeletonBuilder.FindSkeletonRoots(objectData.InnerNode, skinJointPaths, _logger);
            _logger.LogInformation("Found {SkinJointCount} skin joints, skeleton roots: {SkeletonRoots}",
                skinJointPaths.Count, string.Join(", ", skeletonRoots.Select(r => r.Name)));
        }
        else
        {
            skeletonRoots = objectData.InnerNode.Children
                .Where(c => !HierarchyExtractor.IsVFXNode(c.Name))
                .ToList();
        }

        if (skeletonRoots.Count > 0)
        {
            objectData.Skeleton = SkeletonBuilder.BuildSkeletonData(
                skeletonRoots,
                skinJointPaths,
                HierarchyExtractor.IsVFXNode,
                _logger
            );
            _logger.LogInformation("Built skeleton with {BoneCount} bones", objectData.Skeleton.Bones.Count);

            objectData.LookPointPosition = ExtractLookPointPosition(objectData.Skeleton);
        }

        _logger.LogInformation("Extracting meshes");
        objectData.Meshes = _meshDataExtractor.ExtractMeshesFromMapObject(objectData.InnerNode, objectData);
        _logger.LogInformation("Total meshes: {MeshCount}", objectData.Meshes.Count);

        // Check for animations - find ALL animator nodes
        var animatorNodes = new List<HierarchyNode>();
        FindAllAnimatorNodesRecursive(rootNode, animatorNodes);

        if (animatorNodes.Count > 0)
        {
            _logger.LogInformation("Found {AnimatorCount} Animator(s): {AnimatorNames}",
                animatorNodes.Count, string.Join(", ", animatorNodes.Select(n => n.Name)));
            objectData.Animations = new List<AnimationData>();

            foreach (var animatorNode in animatorNodes)
            {
                ExtractPrefabAnimationsCore(objectData.Skeleton, objectData.Animations, animatorNode);
            }

            if (objectData.Animations.Count > 1)
            {
                objectData.Animations = MergeMultipleAnimatorAnimations(objectData.Animations);
            }

            TryApplyIdlePoseAsRestPose(objectData);
        }

        _logger.LogInformation("Map object extraction complete");
        return objectData;
    }

    #endregion

    #region GameObject Extraction

    public GameObjectData ExtractGameObject(string gameObjectName)
    {
        _logger.LogInformation("Extracting GameObject: {GameObjectName}", gameObjectName);

        var prefabData = new GameObjectData { Name = gameObjectName };

        // First try resource path lookup (future-proof for duplicates)
        var prefabRoot = FindPrefabByNameWithResourcePaths(gameObjectName);
        if (prefabRoot != null)
        {
            _logger.LogInformation("Found GameObject by resource path (PathID: {PathId})", prefabRoot.PathID);
        }
        else
        {
            // Fall back to name-based lookup
            prefabRoot = FindPrefabByName(gameObjectName);
            if (prefabRoot != null)
            {
                _logger.LogInformation("Found GameObject by name (PathID: {PathId})", prefabRoot.PathID);
            }
        }

        if (prefabRoot == null)
        {
            throw new Exception($"GameObject not found: '{gameObjectName}'");
        }

        _logger.LogInformation("Found GameObject root: {GameObjectRootName}", prefabRoot.Name);

        var rootNode = _hierarchyExtractor.BuildHierarchyNode(prefabRoot);
        if (rootNode == null)
        {
            throw new Exception($"Failed to build hierarchy for GameObject: {gameObjectName}");
        }

        prefabData.RootNode = rootNode;
        prefabData.WrapperNode = rootNode;
        prefabData.InnerNode = rootNode;

        // Get animator transform PathID for consistent bone path building
        var animatorTransformPathId = _boneDataExtractor.GetAnimatorTransformPathId(rootNode);

        // Check for skinned meshes recursively and extract their bone paths
        var skinJointPaths = _boneDataExtractor.ExtractSkinJointPathsRecursive(rootNode, animatorTransformPathId);

        List<HierarchyNode> skeletonRoots;
        if (skinJointPaths.Count > 0)
        {
            skeletonRoots = SkeletonBuilder.FindSkeletonRoots(rootNode, skinJointPaths, _logger);
            _logger.LogInformation("Found {SkinJointCount} skin joints, skeleton roots: {SkeletonRoots}",
                skinJointPaths.Count, string.Join(", ", skeletonRoots.Select(r => r.Name)));
        }
        else
        {
            skeletonRoots = rootNode.Children
                .Where(c => !HierarchyExtractor.IsVFXNode(c.Name))
                .ToList();
        }

        if (skeletonRoots.Count > 0)
        {
            prefabData.Skeleton = SkeletonBuilder.BuildSkeletonData(
                skeletonRoots,
                skinJointPaths,
                HierarchyExtractor.IsVFXNode,
                _logger
            );
            _logger.LogInformation("Built skeleton with {BoneCount} bones", prefabData.Skeleton.Bones.Count);
        }

        _logger.LogInformation("Extracting meshes from GameObject hierarchy");
        prefabData.Meshes = _meshDataExtractor.ExtractMeshesFromGameObject(rootNode, prefabData);
        _logger.LogInformation("Total meshes extracted: {MeshCount}", prefabData.Meshes.Count);

        // Check for animations - find ALL animator nodes
        var animatorNodes = new List<HierarchyNode>();
        FindAllAnimatorNodesRecursive(rootNode, animatorNodes);

        if (animatorNodes.Count > 0)
        {
            _logger.LogInformation("Found {AnimatorCount} Animator(s): {AnimatorNames}",
                animatorNodes.Count, string.Join(", ", animatorNodes.Select(n => n.Name)));
            prefabData.Animations = new List<AnimationData>();

            foreach (var animatorNode in animatorNodes)
            {
                ExtractPrefabAnimationsCore(prefabData.Skeleton, prefabData.Animations, animatorNode);
            }

            if (prefabData.Animations.Count > 1)
            {
                prefabData.Animations = MergeMultipleAnimatorAnimations(prefabData.Animations);
            }

            TryApplyIdlePoseAsRestPose(prefabData);
        }

        _logger.LogInformation("Total materials: {MaterialCount}", prefabData.Materials.Count);
        _logger.LogInformation("Total textures: {TextureCount}", prefabData.Textures.Count);

        _logger.LogInformation("GameObject extraction complete");
        return prefabData;
    }

    #endregion

    #region Universal Extraction

    public PrefabData ExtractPrefab(string prefabName, PrefabType? typeHint = null)
    {
        using (_logger.BeginScope("Prefab: {PrefabName}", prefabName))
        {
            if (typeHint == null)
            {
                typeHint = InferPrefabType(prefabName);
            }

            return typeHint switch
            {
                PrefabType.Unit => ExtractUnit(prefabName),
                PrefabType.MapObject => ExtractMapObject(prefabName),
                _ => throw new ArgumentException($"Unsupported prefab type: {typeHint}")
            };
        }
    }

    private PrefabType InferPrefabType(string name)
    {
        if (name.Contains('/'))
        {
            string lower = name.ToLowerInvariant();
            if (lower.StartsWith("interactive/") ||
                lower.StartsWith("resource/") ||
                lower.StartsWith("barracks/") ||
                lower.StartsWith("objects/") ||
                lower.StartsWith("artifact/"))
            {
                return PrefabType.MapObject;
            }
        }

        return PrefabType.Unit;
    }

    #endregion

    #region List Methods

    public List<string> ListUnits()
    {
        var provider = new UnitListProvider(_assetLoader.AssetPath);
        return provider.ListUnits();
    }

    public List<PrefabDescriptor> ListPrefabs(PrefabType type)
    {
        return type switch
        {
            PrefabType.Unit => new UnitListProvider(_assetLoader.AssetPath).ListPrefabs(),
            PrefabType.MapObject => new MapObjectListProvider(_assetLoader.AssetPath).ListPrefabs(),
            _ => new List<PrefabDescriptor>()
        };
    }

    public List<string> ListMapObjects()
    {
        var provider = new MapObjectListProvider(_assetLoader.AssetPath);
        return provider.ListMapObjects();
    }

    public void ClearMaterialCaches()
    {
        _materialExtractor.ClearCaches();
        _logger.LogInformation("Cleared material and texture caches");
    }

    #endregion

    #region Animation Helpers

    private void FindAllAnimatorNodesRecursive(HierarchyNode node, List<HierarchyNode> results)
    {
        if (!node.IsActive)
        {
            return;
        }

        var gameObject = FindGameObjectByPathId(node.SourceFile, node.PathID);
        if (gameObject != null && gameObject.TryGetComponent<IAnimator>(out _))
        {
            results.Add(node);
        }

        foreach (var child in node.Children)
        {
            FindAllAnimatorNodesRecursive(child, results);
        }
    }

    private List<AnimationData> MergeMultipleAnimatorAnimations(List<AnimationData> animations)
    {
        const float DURATION_EPSILON = 0.1f;

        var groups = new List<List<AnimationData>>();
        foreach (var anim in animations)
        {
            var matchingGroup = groups.FirstOrDefault(g =>
                Math.Abs(g[0].Length - anim.Length) < DURATION_EPSILON);

            if (matchingGroup != null)
            {
                matchingGroup.Add(anim);
            }
            else
            {
                groups.Add(new List<AnimationData> { anim });
            }
        }

        var merged = new List<AnimationData>();
        foreach (var group in groups)
        {
            if (group.Count == 1)
            {
                merged.Add(group[0]);
                continue;
            }

            var first = group[0];
            var mergedAnim = new AnimationData
            {
                Name = first.Name.Split('_')[0] + "_combined",
                Length = first.Length,
                FrameRate = first.FrameRate,
                Channels = new List<AnimationChannel>()
            };

            foreach (var anim in group)
            {
                mergedAnim.Channels.AddRange(anim.Channels);
            }

            merged.Add(mergedAnim);
            _logger.LogInformation("Merged {AnimationCount} animations into '{MergedAnimName}' ({ChannelCount} channels)",
                group.Count, mergedAnim.Name, mergedAnim.Channels.Count);
        }

        return merged;
    }

    private void ExtractPrefabAnimationsCore(
        SkeletonData? skeleton,
        List<AnimationData> animationsList,
        HierarchyNode animatorNode)
    {
        if (skeleton == null || skeleton.Bones.Count == 0)
        {
            _logger.LogWarning("No skeleton available for animation extraction for {AnimatorName}", animatorNode.Name);
            return;
        }

        var animatorBone = skeleton.Bones.FirstOrDefault(b => b.Name == animatorNode.Name);
        string animatorPath = animatorBone?.Path ?? "";

        _logger.LogInformation("Animator '{AnimatorName}' path in skeleton: '{AnimatorPath}'", animatorNode.Name, animatorPath);

        var animations = _animationDataExtractor.ExtractAnimations(
            animatorNode,
            skeleton,
            new List<string>()
        );

        if (animations.Count == 0)
        {
            _logger.LogInformation("No animations extracted from '{AnimatorName}'", animatorNode.Name);
            return;
        }

        foreach (var anim in animations)
        {
            foreach (var channel in anim.Channels)
            {
                if (string.IsNullOrEmpty(channel.BonePath) || channel.BonePath == "")
                {
                    channel.BonePath = animatorPath;
                }
                else if (!string.IsNullOrEmpty(animatorPath))
                {
                    channel.BonePath = $"{animatorPath}/{channel.BonePath}";
                }
            }
        }

        animationsList.AddRange(animations);
        _logger.LogInformation("Extracted {AnimationCount} animation(s) from '{AnimatorName}'", animations.Count, animatorNode.Name);
    }

    #endregion

    #region Pose Application

    private void TryApplyIdlePoseAsRestPose(PrefabData prefabData)
    {
        (SkeletonData? skeleton, IReadOnlyList<AnimationData>? animations) = prefabData switch
        {
            UnitData ud => (ud.Skeleton, ud.Animations),
            MapObjectData mod => (mod.Skeleton, mod.Animations),
            GameObjectData god => (god.Skeleton, god.Animations),
            _ => (null, null)
        };

        if (skeleton == null || animations == null)
        {
            bool isKnownType = prefabData is UnitData or MapObjectData or GameObjectData;
            if (!isKnownType)
            {
                _logger.LogWarning("TryApplyIdlePoseAsRestPose: Unhandled prefab type '{PrefabTypeName}'", prefabData.GetType().Name);
            }
            return;
        }

        TryApplyIdlePoseAsRestPose(skeleton, animations);
    }

    private void TryApplyIdlePoseAsRestPose(SkeletonData? skeleton, IReadOnlyList<AnimationData> animations)
    {
        if (skeleton == null || skeleton.Bones.Count == 0)
        {
            return;
        }

        var idle = SelectIdleAnimation(animations);
        if (idle == null)
        {
            _logger.LogDebug("[POSE] No idle animation found; leaving bind pose as default");
            return;
        }

        var bonesByPath = skeleton.Bones.ToDictionary(b => b.Path, StringComparer.Ordinal);
        var touchedBones = new HashSet<string>(StringComparer.Ordinal);

        foreach (var channel in idle.Channels)
        {
            if (string.IsNullOrWhiteSpace(channel.BonePath))
            {
                continue;
            }

            if (!bonesByPath.TryGetValue(channel.BonePath, out var bone))
            {
                continue;
            }

            float value = SampleCurveAtTime(channel.Keyframes, 0f);
            ApplyChannelValue(bone.LocalTransform, channel.Property, value);
            touchedBones.Add(bone.Path);
        }

        foreach (var bone in skeleton.Bones)
        {
            if (!touchedBones.Contains(bone.Path))
            {
                continue;
            }

            NormalizeQuaternionInPlace(bone.LocalTransform.Rotation);
        }

        _logger.LogInformation("[POSE] Default pose set from '{IdleAnimName}' (t=0) for {TouchedBoneCount} bones", idle.Name, touchedBones.Count);
    }

    private static AnimationData? SelectIdleAnimation(IReadOnlyList<AnimationData> animations)
    {
        if (animations == null || animations.Count == 0)
        {
            return null;
        }

        AnimationData? best = null;
        int bestScore = int.MinValue;

        foreach (var anim in animations)
        {
            if (anim == null)
            {
                continue;
            }

            var name = (anim.Name ?? string.Empty).Trim().ToLowerInvariant();
            if (name.Length == 0)
            {
                continue;
            }

            int score = 0;
            if (name == "idle")
            {
                score += 10_000;
            }
            else if (name.StartsWith("idle_", StringComparison.Ordinal))
            {
                score += 8_000;
            }
            else if (name.StartsWith("idle", StringComparison.Ordinal))
            {
                score += 6_000;
            }
            else if (name.Contains("idle", StringComparison.Ordinal))
            {
                score += 3_000;
            }
            else
            {
                continue;
            }

            score += Math.Min(anim.Channels?.Count ?? 0, 500);

            if (best == null || score > bestScore)
            {
                best = anim;
                bestScore = score;
            }
        }

        return best;
    }

    private static void ApplyChannelValue(Models.Transform target, AnimationProperty property, float value)
    {
        switch (property)
        {
            case AnimationProperty.PositionX: target.Position.X = value; break;
            case AnimationProperty.PositionY: target.Position.Y = value; break;
            case AnimationProperty.PositionZ: target.Position.Z = value; break;
            case AnimationProperty.RotationX: target.Rotation.X = value; break;
            case AnimationProperty.RotationY: target.Rotation.Y = value; break;
            case AnimationProperty.RotationZ: target.Rotation.Z = value; break;
            case AnimationProperty.RotationW: target.Rotation.W = value; break;
            case AnimationProperty.ScaleX: target.Scale.X = value; break;
            case AnimationProperty.ScaleY: target.Scale.Y = value; break;
            case AnimationProperty.ScaleZ: target.Scale.Z = value; break;
        }
    }

    private static float SampleCurveAtTime(IReadOnlyList<Keyframe> keyframes, float time)
    {
        if (keyframes == null || keyframes.Count == 0 || !float.IsFinite(time))
        {
            return 0f;
        }

        var keys = keyframes
            .Where(k => float.IsFinite(k.Time) && float.IsFinite(k.Value))
            .OrderBy(k => k.Time)
            .ToList();

        if (keys.Count == 0)
        {
            return 0f;
        }

        if (time <= keys[0].Time + POSE_TIME_EPSILON)
        {
            return keys[0].Value;
        }

        if (time >= keys[^1].Time - POSE_TIME_EPSILON)
        {
            return keys[^1].Value;
        }

        for (int i = 0; i < keys.Count - 1; i++)
        {
            var k0 = keys[i];
            var k1 = keys[i + 1];

            if (time > k1.Time + POSE_TIME_EPSILON)
            {
                continue;
            }

            float dt = k1.Time - k0.Time;
            if (dt <= POSE_TIME_EPSILON)
            {
                return k0.Value;
            }

            float u = (time - k0.Time) / dt;
            u = Math.Clamp(u, 0f, 1f);

            float m0 = k0.OutTangent;
            float m1 = k1.InTangent;

            if (!float.IsFinite(m0) || !float.IsFinite(m1))
            {
                return k0.Value + (k1.Value - k0.Value) * u;
            }

            float u2 = u * u;
            float u3 = u2 * u;

            float h00 = 2f * u3 - 3f * u2 + 1f;
            float h10 = u3 - 2f * u2 + u;
            float h01 = -2f * u3 + 3f * u2;
            float h11 = u3 - u2;

            float value = h00 * k0.Value + h10 * (m0 * dt) + h01 * k1.Value + h11 * (m1 * dt);
            return float.IsFinite(value) ? value : k0.Value;
        }

        return keys[^1].Value;
    }

    private static void NormalizeQuaternionInPlace(Models.Quaternion q)
    {
        if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W))
        {
            q.X = 0f; q.Y = 0f; q.Z = 0f; q.W = 1f;
            return;
        }

        float lenSq = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
        if (lenSq <= 0f || !float.IsFinite(lenSq))
        {
            q.X = 0f; q.Y = 0f; q.Z = 0f; q.W = 1f;
            return;
        }

        float invLen = 1f / MathF.Sqrt(lenSq);
        q.X *= invLen;
        q.Y *= invLen;
        q.Z *= invLen;
        q.W *= invLen;
    }

    // Extract look_point position from skeleton for orientation correction
    private Models.Vector3? ExtractLookPointPosition(SkeletonData skeleton)
    {
        var lookPointBone = skeleton.Bones.FirstOrDefault(b =>
            string.Equals(b.Name, "look_point", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.Name, "Look_point", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.Name, "lookpoint", StringComparison.OrdinalIgnoreCase));

        if (lookPointBone == null)
        {
            return null;
        }

        var pos = lookPointBone.LocalTransform.Position;

        const float epsilon = 0.05f;
        float xzLengthSq = pos.X * pos.X + pos.Z * pos.Z;
        if (xzLengthSq < epsilon * epsilon)
        {
            _logger.LogInformation("look_point at ({X:F3}, {Y:F3}, {Z:F3}) has no X/Z direction - using default rotation", pos.X, pos.Y, pos.Z);
            return null;
        }

        _logger.LogInformation("Found look_point at ({X:F3}, {Y:F3}, {Z:F3})", pos.X, pos.Y, pos.Z);
        return pos;
    }

    #endregion

    #region Texture Extraction

    public List<(string Name, AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D Texture)> ExtractTextures(string prefabName)
    {
        _logger.LogInformation("Extracting textures for prefab: {PrefabName}", prefabName);

        var textures = new List<(string Name, AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D Texture)>();
        var seenTextureIds = new HashSet<long>();

        IGameObject? prefabRoot = null;
        if (prefabName.Contains('/'))
        {
            prefabRoot = FindPrefabByResourcePath(prefabName);
        }

        if (prefabRoot == null)
        {
            prefabRoot = FindPrefabByNameWithResourcePaths(prefabName);
        }

        if (prefabRoot == null)
        {
            prefabRoot = FindPrefabByName(prefabName);
        }

        if (prefabRoot == null)
        {
            _logger.LogWarning("Prefab not found for texture extraction: {PrefabName}", prefabName);
            return textures;
        }

        _logger.LogInformation("Found prefab (PathID: {PathId})", prefabRoot.PathID);
        ExtractTexturesRecursive(prefabRoot, textures, seenTextureIds);

        _logger.LogInformation("Found {TextureCount} unique textures for '{PrefabName}'", textures.Count, prefabName);
        return textures;
    }

    private void ExtractTexturesRecursive(
        IGameObject gameObject,
        List<(string Name, AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D Texture)> textures,
        HashSet<long> seenTextureIds)
    {
        if (gameObject.TryGetComponent<ISkinnedMeshRenderer>(out var smr))
        {
            ExtractTexturesFromRenderer(smr, textures, seenTextureIds);
        }

        if (gameObject.TryGetComponent<IMeshRenderer>(out var meshRenderer))
        {
            ExtractTexturesFromRenderer(meshRenderer, textures, seenTextureIds);
        }

        if (gameObject.TryGetComponent<ITransform>(out var transform))
        {
            foreach (var childTransform in transform.Children_C4P.WhereNotNull())
            {
                var childGO = childTransform.GameObject_C4P;
                if (childGO != null)
                {
                    ExtractTexturesRecursive(childGO, textures, seenTextureIds);
                }
            }
        }
    }

    private void ExtractTexturesFromRenderer(
        IUnityObjectBase renderer,
        List<(string Name, AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D Texture)> textures,
        HashSet<long> seenTextureIds)
    {
        if (renderer is ISkinnedMeshRenderer smr)
        {
            foreach (var materialPPtr in smr.Materials_C25)
            {
                var materialAsset = materialPPtr.TryGetAsset(renderer.Collection);
                if (materialAsset is AssetRipper.SourceGenerated.Classes.ClassID_21.IMaterial material)
                {
                    ExtractTexturesFromMaterial(material, textures, seenTextureIds);
                }
            }
        }
        else if (renderer is IMeshRenderer mr)
        {
            foreach (var materialPPtr in mr.Materials_C25)
            {
                var materialAsset = materialPPtr.TryGetAsset(renderer.Collection);
                if (materialAsset is AssetRipper.SourceGenerated.Classes.ClassID_21.IMaterial material)
                {
                    ExtractTexturesFromMaterial(material, textures, seenTextureIds);
                }
            }
        }
        else
        {
            _logger.LogWarning("ExtractTexturesFromRenderer: Unhandled renderer type '{RendererType}'", renderer.GetType().Name);
        }
    }

    private void ExtractTexturesFromMaterial(
        AssetRipper.SourceGenerated.Classes.ClassID_21.IMaterial material,
        List<(string Name, AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D Texture)> textures,
        HashSet<long> seenTextureIds)
    {
        string[] texturePropertyNames = { "_MainTex", "_BaseMap", "_BumpMap", "_NormalMap", "_EmissionMap" };

        foreach (var propName in texturePropertyNames)
        {
            if (material.TryGetTextureProperty(propName, out var texEnv))
            {
                var textureAsset = texEnv.Texture.TryGetAsset(material.Collection);
                if (textureAsset is AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D texture)
                {
                    if (seenTextureIds.Add(texture.PathID))
                    {
                        var name = texture.Name ?? $"texture_{texture.PathID}";
                        textures.Add((name, texture));
                        _logger.LogInformation("  Found texture: {TextureName} (PathID: {PathId})", name, texture.PathID);
                    }
                }
            }
        }
    }

    public List<(string Name, AssetRipper.SourceGenerated.Classes.ClassID_28.ITexture2D Texture)> SearchTextures(string namePattern)
        => _assetLoader.SearchTextures(namePattern);

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _assetLoader?.Dispose();
            _disposed = true;
        }
    }
}
