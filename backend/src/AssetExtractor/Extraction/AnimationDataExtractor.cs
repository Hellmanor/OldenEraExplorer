#nullable enable
using AssetRipper.Assets.Bundles;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.Processing;
using AssetRipper.Processing.AnimationClips;
using AssetRipper.SourceGenerated.Classes.ClassID_74;
using AssetRipper.SourceGenerated.Classes.ClassID_91;
using AssetRipper.SourceGenerated.Classes.ClassID_95;
using AssetRipper.SourceGenerated.Classes.ClassID_221;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.QuaternionCurve;
using AssetRipper.SourceGenerated.Subclasses.Vector3Curve;
using AssetExtractor.Models;
using AssetExtractor.Extraction.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

// Uses AssetRipper's AnimationClipConverter for curve processing
public class AnimationDataExtractor : IAnimationDataExtractor
{
    private readonly ILogger<AnimationDataExtractor> _logger;
    private readonly IGameObjectProvider _gameObjectProvider;
    private readonly GameBundle _gameBundle;
    private readonly PathChecksumCache _checksumCache;

    public AnimationDataExtractor(
        IGameObjectProvider gameObjectProvider,
        GameBundle gameBundle,
        ILogger<AnimationDataExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<AnimationDataExtractor>.Instance;
        _gameObjectProvider = gameObjectProvider;
        _gameBundle = gameBundle;

        // Create PathChecksumCache using GameData
        // This builds a cache of all bone paths from Avatars, Animators, and Animations in the bundle
        var assemblyManager = new BaseManager(_ => { });
        var projectVersion = gameBundle.GetMaxUnityVersion();
        var gameData = new GameData(gameBundle, projectVersion, assemblyManager, null);
        _checksumCache = new PathChecksumCache(gameData);
    }

    public List<AnimationData> ExtractAnimations(
        HierarchyNode inner,
        SkeletonData skeleton,
        List<string> skinJointPaths)
    {
        var animations = new List<AnimationData>();

        try
        {
            var gameObject = _gameObjectProvider.FindGameObjectByPathId(inner.SourceFile, inner.PathID);
            if (gameObject == null)
            {
                _logger.LogWarning(
                    "Cannot find IGameObject for inner node: {InnerNodeName}",
                    inner.Name);
                return animations;
            }

            if (!gameObject.TryGetComponent<IAnimator>(out var animator))
            {
                _logger.LogWarning(
                    "No Animator component found on node: {NodeName}",
                    inner.Name);
                return animations;
            }

            _logger.LogInformation(
                "Found Animator component on node: {NodeName}",
                inner.Name);

            // Build path-to-bone-index mapping for animation channel resolution
            // AND add paths to checksum cache so AnimationClipConverter can resolve them
            var pathToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var bone in skeleton.Bones)
            {
                if (!string.IsNullOrEmpty(bone.Path) && !pathToIndex.ContainsKey(bone.Path))
                {
                    pathToIndex[bone.Path] = bone.Index;
                    // Add to checksum cache for path hash resolution
                    _checksumCache.Add(bone.Path);
                }
            }

            // Also add hierarchy paths (relative to animator node)
            AddHierarchyPathsToCache(inner, "");

            var clips = FindAnimationClips(animator);
            _logger.LogInformation(
                "Found {ClipCount} animation clips",
                clips.Count);

            var usedAnimationNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clip in clips)
            {
                var animData = ExtractSingleClip(clip, pathToIndex);
                if (animData != null && animData.Channels.Count > 0)
                {
                    animData.Name = MakeUniqueName(NormalizeAnimationName(animData.Name), usedAnimationNames);
                    animations.Add(animData);
                    _logger.LogInformation(
                        "  Animation: {AnimationName} ({Duration:F2}s)",
                        animData.Name,
                        animData.Length);
                }
            }

            _logger.LogInformation(
                "Successfully extracted {AnimationCount} animations",
                animations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract animations");
        }

        return animations;
    }

    private List<IAnimationClip> FindAnimationClips(IAnimator animator)
    {
        var clips = new List<IAnimationClip>();
        var seenPathIds = new HashSet<long>();

        AssetRipper.Assets.IUnityObjectBase? controllerAsset = null;

        if (animator.Has_Controller_PPtr_AnimatorController_4())
        {
            controllerAsset = animator.Controller_PPtr_AnimatorController_4P;
        }
        else if (animator.Has_Controller_PPtr_RuntimeAnimatorController_4_3())
        {
            controllerAsset = animator.Controller_PPtr_RuntimeAnimatorController_4_3P;
        }
        else if (animator.Has_Controller_PPtr_RuntimeAnimatorController_5())
        {
            controllerAsset = animator.Controller_PPtr_RuntimeAnimatorController_5P;
        }

        if (controllerAsset == null)
        {
            _logger.LogDebug("Animator has no controller reference");
            return clips;
        }

        _logger.LogDebug(
            "Controller type: {ControllerType}",
            controllerAsset.GetType().Name);

        if (controllerAsset is IAnimatorController animatorController)
        {
            foreach (var clipPPtr in animatorController.AnimationClips)
            {
                if (clipPPtr.TryGetAsset(animatorController.Collection, out var clip) && seenPathIds.Add(clip.PathID))
                {
                    clips.Add(clip);
                }
            }
        }
        else if (controllerAsset is IAnimatorOverrideController overrideController)
        {
            _logger.LogInformation(
                "Found AnimatorOverrideController with {ClipOverrideCount} clip overrides",
                overrideController.Clips.Count);

            foreach (var overrideClip in overrideController.Clips)
            {
                if (overrideClip.OverrideClip.TryGetAsset(overrideController.Collection, out var clip) && seenPathIds.Add(clip.PathID))
                {
                    clips.Add(clip);
                }
            }
        }
        else
        {
            _logger.LogInformation(
                "Controller is not standard AnimatorController, searching GameBundle for clips");

            foreach (var asset in _gameBundle.FetchAssets())
            {
                if (asset is IAnimationClip clip)
                {
                    if (seenPathIds.Add(clip.PathID))
                    {
                        clips.Add(clip);
                    }
                }
            }
        }

        return clips;
    }

    private AnimationData? ExtractSingleClip(
        IAnimationClip clip,
        Dictionary<string, int> pathToIndex)
    {
        try
        {
            var name = clip.Name ?? "animation";
            var sampleRate = clip.SampleRate_C74;

            _logger.LogDebug(
                "Processing clip: {ClipName}, sample rate={SampleRate}",
                name,
                sampleRate);

            if (!clip.Has_ClipBindingConstant_C74())
            {
                _logger.LogWarning(
                    "Clip '{ClipName}' has no ClipBindingConstant",
                    name);
                return null;
            }

            if (!clip.Has_MuscleClip_C74())
            {
                _logger.LogWarning(
                    "Clip '{ClipName}' has no MuscleClip data",
                    name);
                return null;
            }

            var muscleClip = clip.MuscleClip_C74;
            float stopTime = muscleClip.StopTime;
            float startTime = muscleClip.StartTime;
            float duration = stopTime - startTime;

            _logger.LogDebug(
                "Clip timing: StartTime={StartTime:F6}, StopTime={StopTime:F6}, Duration={Duration:F6}s",
                startTime,
                stopTime,
                duration);

            // Check if clip was already processed (curves already populated)
            // AnimationClipConverter.Process() ADDS curves, so we should only call it once per clip
            bool alreadyProcessed = clip.PositionCurves_C74.Count > 0
                || clip.RotationCurves_C74.Count > 0
                || clip.ScaleCurves_C74.Count > 0;

            if (!alreadyProcessed)
            {
                // Let AssetRipper do the heavy lifting - process the clip to populate curves
                AnimationClipConverter.Process(clip, _checksumCache);
            }

            var animData = new AnimationData
            {
                Name = name,
                FrameRate = sampleRate,
                Length = duration
            };

            // Extract position curves
            var positionCurves = clip.PositionCurves_C74;
            if (positionCurves.Count > 0)
            {
                foreach (var curve in positionCurves)
                {
                    ExtractVector3Curve(curve, animData, pathToIndex,
                        AnimationProperty.PositionX, AnimationProperty.PositionY, AnimationProperty.PositionZ);
                }
                _logger.LogDebug(
                    "Extracted {PositionCurveCount} position curves",
                    positionCurves.Count);
            }

            // Extract rotation curves (quaternion)
            var rotationCurves = clip.RotationCurves_C74;
            if (rotationCurves.Count > 0)
            {
                foreach (var curve in rotationCurves)
                {
                    ExtractQuaternionCurve(curve, animData, pathToIndex);
                }
                _logger.LogDebug(
                    "Extracted {RotationCurveCount} rotation curves",
                    rotationCurves.Count);
            }

            // Extract scale curves
            var scaleCurves = clip.ScaleCurves_C74;
            if (scaleCurves.Count > 0)
            {
                foreach (var curve in scaleCurves)
                {
                    ExtractVector3Curve(curve, animData, pathToIndex,
                        AnimationProperty.ScaleX, AnimationProperty.ScaleY, AnimationProperty.ScaleZ);
                }
                _logger.LogDebug(
                    "Extracted {ScaleCurveCount} scale curves",
                    scaleCurves.Count);
            }

            return animData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract clip '{ClipName}'", clip.Name);
            return null;
        }
    }

    private static void ExtractVector3Curve(
        IVector3Curve curve,
        AnimationData animData,
        Dictionary<string, int> pathToIndex,
        AnimationProperty propX,
        AnimationProperty propY,
        AnimationProperty propZ)
    {
        string path = curve.Path;
        int boneIndex = ResolveBoneIndex(path, pathToIndex);

        var channelX = new AnimationChannel
        {
            BonePath = path,
            BoneIndex = boneIndex,
            Property = propX,
            Keyframes = new List<Keyframe>()
        };

        var channelY = new AnimationChannel
        {
            BonePath = path,
            BoneIndex = boneIndex,
            Property = propY,
            Keyframes = new List<Keyframe>()
        };

        var channelZ = new AnimationChannel
        {
            BonePath = path,
            BoneIndex = boneIndex,
            Property = propZ,
            Keyframes = new List<Keyframe>()
        };

        foreach (var keyframe in curve.Curve.Curve)
        {
            channelX.Keyframes.Add(new Keyframe
            {
                Time = keyframe.Time,
                Value = keyframe.Value.X,
                InTangent = keyframe.InSlope.X,
                OutTangent = keyframe.OutSlope.X
            });

            channelY.Keyframes.Add(new Keyframe
            {
                Time = keyframe.Time,
                Value = keyframe.Value.Y,
                InTangent = keyframe.InSlope.Y,
                OutTangent = keyframe.OutSlope.Y
            });

            channelZ.Keyframes.Add(new Keyframe
            {
                Time = keyframe.Time,
                Value = keyframe.Value.Z,
                InTangent = keyframe.InSlope.Z,
                OutTangent = keyframe.OutSlope.Z
            });
        }

        if (channelX.Keyframes.Count > 0) animData.Channels.Add(channelX);
        if (channelY.Keyframes.Count > 0) animData.Channels.Add(channelY);
        if (channelZ.Keyframes.Count > 0) animData.Channels.Add(channelZ);
    }

    private static void ExtractQuaternionCurve(
        IQuaternionCurve curve,
        AnimationData animData,
        Dictionary<string, int> pathToIndex)
    {
        string path = curve.Path;
        int boneIndex = ResolveBoneIndex(path, pathToIndex);

        var channelX = new AnimationChannel
        {
            BonePath = path,
            BoneIndex = boneIndex,
            Property = AnimationProperty.RotationX,
            Keyframes = new List<Keyframe>()
        };

        var channelY = new AnimationChannel
        {
            BonePath = path,
            BoneIndex = boneIndex,
            Property = AnimationProperty.RotationY,
            Keyframes = new List<Keyframe>()
        };

        var channelZ = new AnimationChannel
        {
            BonePath = path,
            BoneIndex = boneIndex,
            Property = AnimationProperty.RotationZ,
            Keyframes = new List<Keyframe>()
        };

        var channelW = new AnimationChannel
        {
            BonePath = path,
            BoneIndex = boneIndex,
            Property = AnimationProperty.RotationW,
            Keyframes = new List<Keyframe>()
        };

        foreach (var keyframe in curve.Curve.Curve)
        {
            channelX.Keyframes.Add(new Keyframe
            {
                Time = keyframe.Time,
                Value = keyframe.Value.X,
                InTangent = keyframe.InSlope.X,
                OutTangent = keyframe.OutSlope.X
            });

            channelY.Keyframes.Add(new Keyframe
            {
                Time = keyframe.Time,
                Value = keyframe.Value.Y,
                InTangent = keyframe.InSlope.Y,
                OutTangent = keyframe.OutSlope.Y
            });

            channelZ.Keyframes.Add(new Keyframe
            {
                Time = keyframe.Time,
                Value = keyframe.Value.Z,
                InTangent = keyframe.InSlope.Z,
                OutTangent = keyframe.OutSlope.Z
            });

            channelW.Keyframes.Add(new Keyframe
            {
                Time = keyframe.Time,
                Value = keyframe.Value.W,
                InTangent = keyframe.InSlope.W,
                OutTangent = keyframe.OutSlope.W
            });
        }

        if (channelX.Keyframes.Count > 0) animData.Channels.Add(channelX);
        if (channelY.Keyframes.Count > 0) animData.Channels.Add(channelY);
        if (channelZ.Keyframes.Count > 0) animData.Channels.Add(channelZ);
        if (channelW.Keyframes.Count > 0) animData.Channels.Add(channelW);
    }

    private static int ResolveBoneIndex(string path, Dictionary<string, int> pathToIndex)
    {
        if (string.IsNullOrEmpty(path))
        {
            return -1;
        }

        if (pathToIndex.TryGetValue(path, out int index))
        {
            return index;
        }

        // Path not found - might be a hash-based path like "path_0x12345678"
        return -1;
    }

    private static string NormalizeAnimationName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "animation";
        }

        var name = rawName.Trim();

        // Remove prefix before pipe character (e.g., "Armature|Walk" -> "Walk")
        var lastPipeIndex = name.LastIndexOf('|');
        if (lastPipeIndex >= 0)
        {
            var afterPipe = name[(lastPipeIndex + 1)..].Trim();
            name = !string.IsNullOrEmpty(afterPipe) ? afterPipe : name[..lastPipeIndex].Trim();
        }

        // Remove "Upg_" prefix if present
        if (name.StartsWith("Upg_", StringComparison.OrdinalIgnoreCase))
        {
            name = name[4..];
        }

        // Remove leading numeric prefixes (e.g., "001.Walk" -> "Walk")
        var i = 0;
        while (i < name.Length && char.IsDigit(name[i]))
        {
            i++;
        }
        if (i > 0 && i < name.Length)
        {
            var j = i;
            while (j < name.Length && (name[j] == '.' || name[j] == '_' || name[j] == '-' || name[j] == ' '))
            {
                j++;
            }
            if (j > i)
            {
                name = name[j..];
            }
        }

        // Normalize to lowercase with underscores
        var sb = new System.Text.StringBuilder(name.Length);
        var lastWasUnderscore = false;
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasUnderscore = false;
            }
            else if (!lastWasUnderscore)
            {
                sb.Append('_');
                lastWasUnderscore = true;
            }
        }

        var normalized = sb.ToString().Trim('_');
        return normalized.Length > 0 ? normalized : "animation";
    }

    private static string MakeUniqueName(string baseName, HashSet<string> usedNames)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "animation" : baseName;
        if (usedNames.Add(name))
        {
            return name;
        }

        var suffix = 2;
        while (true)
        {
            var candidate = $"{name}_{suffix}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
            suffix++;
        }
    }

    // Unity animation paths are relative to Animator node - populate cache for path resolution
    private void AddHierarchyPathsToCache(HierarchyNode node, string currentPath)
    {
        // Add the current path to cache
        if (!string.IsNullOrEmpty(currentPath))
        {
            _checksumCache.Add(currentPath);
        }

        // Recurse to children
        foreach (var child in node.Children)
        {
            string childPath = string.IsNullOrEmpty(currentPath)
                ? child.Name
                : $"{currentPath}/{child.Name}";
            AddHierarchyPathsToCache(child, childPath);
        }
    }
}
