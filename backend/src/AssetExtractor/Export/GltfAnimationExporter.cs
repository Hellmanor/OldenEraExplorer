#nullable enable
using AssetExtractor.Models;
using AssetExtractor.Export.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpGLTF.Scenes;

namespace AssetExtractor.Export;

public class GltfAnimationExporter : IAnimationExporter
{
    private readonly ILogger<GltfAnimationExporter> _logger;
    private const float ANIMATION_TIME_EPSILON = 1e-6f;
    private const float ANIMATION_VALUE_EPSILON = 1e-5f;

    public GltfAnimationExporter(ILogger<GltfAnimationExporter>? logger = null)
    {
        _logger = logger ?? NullLogger<GltfAnimationExporter>.Instance;
    }

    public void AddAnimations(
        SceneBuilder scene,
        List<AnimationData> animations,
        SkeletonData skeleton,
        Dictionary<string, NodeBuilder> nodeBuilders)
    {
        foreach (var anim in animations)
        {
            try
            {
                _logger.LogInformation("  Adding animation: {AnimationName} ({LengthSeconds:F2}s, {ChannelCount} channels)",
                    anim.Name, anim.Length, anim.Channels.Count);

                float frameRate = float.IsFinite(anim.FrameRate) && anim.FrameRate > 0 ? anim.FrameRate : 30f;
                float clipEndTime = anim.Length > 0 ? anim.Length : 1f / frameRate;
                var bakedTimes = BuildBakedTimes(anim.Length, anim.FrameRate);
                var constantTimes = new[] { 0f, clipEndTime };

                var channelsByBone = anim.Channels
                    .GroupBy(ch => ch.BonePath)
                    .ToDictionary(g => g.Key, g => g.ToList());

                int skippedPlaceholders = 0;
                int skippedOther = 0;
                int processedBones = 0;

                foreach (var bonePath in channelsByBone.Keys)
                {
                    if (!nodeBuilders.TryGetValue(bonePath, out var nodeBuilder))
                    {
                        if (bonePath.StartsWith("path_0x"))
                        {
                            skippedPlaceholders++;
                        }
                        else
                        {
                            skippedOther++;
                        }
                        continue;
                    }

                    processedBones++;
                    var channels = channelsByBone[bonePath];
                    var bone = skeleton.Bones.FirstOrDefault(b => b.Path == bonePath);
                    string boneName = bone?.Name ?? bonePath;

                    ProcessRotationTrack(channels, nodeBuilder, anim, clipEndTime, constantTimes, bakedTimes, bonePath, boneName);
                    ProcessPositionTrack(channels, nodeBuilder, anim, clipEndTime, constantTimes, bakedTimes, bonePath, boneName);
                    ProcessScaleTrack(channels, nodeBuilder, anim, clipEndTime, constantTimes, bakedTimes, bonePath, boneName);
                }

                _logger.LogInformation("  Animation '{AnimationName}': {ProcessedBoneCount} bones, skipped {SkippedCount}",
                    anim.Name, processedBones, skippedPlaceholders + skippedOther);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add animation '{AnimationName}'", anim.Name);
            }
        }
    }

    private void ProcessRotationTrack(
        List<AnimationChannel> channels,
        NodeBuilder nodeBuilder,
        AnimationData anim,
        float clipEndTime,
        float[] constantTimes,
        IReadOnlyList<float> bakedTimes,
        string bonePath,
        string boneName)
    {
        var rotationChannels = channels
            .Where(ch => ch.Property is AnimationProperty.RotationX or AnimationProperty.RotationY or AnimationProperty.RotationZ or AnimationProperty.RotationW)
            .ToList();

        if (rotationChannels.Count != 4)
        {
            return;
        }

        var xChannel = rotationChannels.First(ch => ch.Property == AnimationProperty.RotationX);
        var yChannel = rotationChannels.First(ch => ch.Property == AnimationProperty.RotationY);
        var zChannel = rotationChannels.First(ch => ch.Property == AnimationProperty.RotationZ);
        var wChannel = rotationChannels.First(ch => ch.Property == AnimationProperty.RotationW);

        var samplers = new[] {
            new FloatCurveSampler(xChannel.Keyframes),
            new FloatCurveSampler(yChannel.Keyframes),
            new FloatCurveSampler(zChannel.Keyframes),
            new FloatCurveSampler(wChannel.Keyframes)
        };

        bool isConstant = IsConstantCurve(xChannel.Keyframes)
            && IsConstantCurve(yChannel.Keyframes)
            && IsConstantCurve(zChannel.Keyframes)
            && IsConstantCurve(wChannel.Keyframes);

        int maxKeys = new[] { xChannel, yChannel, zChannel, wChannel }.Max(c => c.Keyframes.Count);
        var times = SelectSampleTimes(isConstant, maxKeys, clipEndTime, constantTimes, bakedTimes,
            xChannel.Keyframes, yChannel.Keyframes, zChannel.Keyframes, wChannel.Keyframes);

        var rotationCurve = new Dictionary<float, System.Numerics.Quaternion>();
        System.Numerics.Quaternion? prev = null;

        foreach (var time in times)
        {
            float x = samplers[0].Evaluate(time);
            float y = samplers[1].Evaluate(time);
            float z = samplers[2].Evaluate(time);
            float w = samplers[3].Evaluate(time);

            var unityQuat = new System.Numerics.Quaternion(x, y, z, w);
            var quat = NormalizeSafe(GlbCoordinateConversion.ToGltfQuaternionConvert(unityQuat));

            if (prev.HasValue && System.Numerics.Quaternion.Dot(prev.Value, quat) < 0f)
            {
                quat = new System.Numerics.Quaternion(-quat.X, -quat.Y, -quat.Z, -quat.W);
            }
            prev = quat;
            rotationCurve[time] = quat;
        }

        rotationCurve = ValidateAnimationCurve(rotationCurve, $"{boneName}/rotation", anim.Name);

        if (rotationCurve.Count == 0)
        {
            _logger.LogWarning("No valid rotation keyframes for {BonePath} in {AnimationName}", bonePath, anim.Name);
            return;
        }

        EnsureMinimumKeyframes(rotationCurve, clipEndTime, bonePath, "rotation");
        foreach (var kvp in rotationCurve)
        {
            nodeBuilder.UseRotation(anim.Name).WithPoint(kvp.Key, kvp.Value);
        }
    }

    private void ProcessPositionTrack(
        List<AnimationChannel> channels,
        NodeBuilder nodeBuilder,
        AnimationData anim,
        float clipEndTime,
        float[] constantTimes,
        IReadOnlyList<float> bakedTimes,
        string bonePath,
        string boneName)
    {
        var positionChannels = channels
            .Where(ch => ch.Property is AnimationProperty.PositionX or AnimationProperty.PositionY or AnimationProperty.PositionZ)
            .ToList();

        if (positionChannels.Count != 3)
        {
            return;
        }

        var xChannel = positionChannels.First(ch => ch.Property == AnimationProperty.PositionX);
        var yChannel = positionChannels.First(ch => ch.Property == AnimationProperty.PositionY);
        var zChannel = positionChannels.First(ch => ch.Property == AnimationProperty.PositionZ);

        var samplers = new[] {
            new FloatCurveSampler(xChannel.Keyframes),
            new FloatCurveSampler(yChannel.Keyframes),
            new FloatCurveSampler(zChannel.Keyframes)
        };

        bool isConstant = IsConstantCurve(xChannel.Keyframes)
            && IsConstantCurve(yChannel.Keyframes)
            && IsConstantCurve(zChannel.Keyframes);

        int maxKeys = new[] { xChannel, yChannel, zChannel }.Max(c => c.Keyframes.Count);
        var times = SelectSampleTimes(isConstant, maxKeys, clipEndTime, constantTimes, bakedTimes,
            xChannel.Keyframes, yChannel.Keyframes, zChannel.Keyframes);

        var positionCurve = new Dictionary<float, System.Numerics.Vector3>();
        foreach (var time in times)
        {
            var unityPos = new System.Numerics.Vector3(
                samplers[0].Evaluate(time),
                samplers[1].Evaluate(time),
                samplers[2].Evaluate(time));
            positionCurve[time] = GlbCoordinateConversion.ToGltfVector3Convert(unityPos);
        }

        positionCurve = ValidateAnimationCurve(positionCurve, $"{boneName}/position", anim.Name);

        if (positionCurve.Count == 0)
        {
            _logger.LogWarning("No valid position keyframes for {BonePath} in {AnimationName}", bonePath, anim.Name);
            return;
        }

        EnsureMinimumKeyframes(positionCurve, clipEndTime, bonePath, "position");
        foreach (var kvp in positionCurve)
        {
            nodeBuilder.UseTranslation(anim.Name).WithPoint(kvp.Key, kvp.Value);
        }
    }

    private void ProcessScaleTrack(
        List<AnimationChannel> channels,
        NodeBuilder nodeBuilder,
        AnimationData anim,
        float clipEndTime,
        float[] constantTimes,
        IReadOnlyList<float> bakedTimes,
        string bonePath,
        string boneName)
    {
        var scaleChannels = channels
            .Where(ch => ch.Property is AnimationProperty.ScaleX or AnimationProperty.ScaleY or AnimationProperty.ScaleZ)
            .ToList();

        if (scaleChannels.Count != 3)
        {
            return;
        }

        var xChannel = scaleChannels.First(ch => ch.Property == AnimationProperty.ScaleX);
        var yChannel = scaleChannels.First(ch => ch.Property == AnimationProperty.ScaleY);
        var zChannel = scaleChannels.First(ch => ch.Property == AnimationProperty.ScaleZ);

        var samplers = new[] {
            new FloatCurveSampler(xChannel.Keyframes),
            new FloatCurveSampler(yChannel.Keyframes),
            new FloatCurveSampler(zChannel.Keyframes)
        };

        bool isConstant = IsConstantCurve(xChannel.Keyframes)
            && IsConstantCurve(yChannel.Keyframes)
            && IsConstantCurve(zChannel.Keyframes);

        int maxKeys = new[] { xChannel, yChannel, zChannel }.Max(c => c.Keyframes.Count);
        var times = SelectSampleTimes(isConstant, maxKeys, clipEndTime, constantTimes, bakedTimes,
            xChannel.Keyframes, yChannel.Keyframes, zChannel.Keyframes);

        var scaleCurve = new Dictionary<float, System.Numerics.Vector3>();
        foreach (var time in times)
        {
            scaleCurve[time] = new System.Numerics.Vector3(
                samplers[0].Evaluate(time),
                samplers[1].Evaluate(time),
                samplers[2].Evaluate(time));
        }

        scaleCurve = ValidateAnimationCurve(scaleCurve, $"{boneName}/scale", anim.Name);

        if (scaleCurve.Count == 0)
        {
            _logger.LogWarning("No valid scale keyframes for {BonePath} in {AnimationName}", bonePath, anim.Name);
            return;
        }

        EnsureMinimumKeyframes(scaleCurve, clipEndTime, bonePath, "scale");
        foreach (var kvp in scaleCurve)
        {
            nodeBuilder.UseScale(anim.Name).WithPoint(kvp.Key, kvp.Value);
        }
    }

    private static IReadOnlyList<float> BuildBakedTimes(float lengthSeconds, float frameRate)
    {
        float length = lengthSeconds;
        if (!float.IsFinite(length) || length < 0f)
        {
            length = 0f;
        }

        float rate = frameRate;
        if (!float.IsFinite(rate) || rate <= 0f)
        {
            rate = 30f;
        }

        int sampleCount = (int)MathF.Ceiling(length * rate);
        if (sampleCount < 2)
        {
            sampleCount = 2;
        }

        if (length <= 0f)
        {
            float dt = 1f / rate;
            return new[] { 0f, dt };
        }

        float step = length / sampleCount;
        var times = new float[sampleCount];
        for (int i = 0; i < sampleCount - 1; i++)
        {
            times[i] = i * step;
        }
        times[sampleCount - 1] = length;
        return times;
    }

    private static IReadOnlyList<float> BuildUnionTimes(float clipEndTime, params IReadOnlyList<Keyframe>[] channels)
    {
        var times = new List<float>();
        times.Add(0f);

        foreach (var channel in channels)
        {
            for (int i = 0; i < channel.Count; i++)
            {
                float t = channel[i].Time;
                if (!float.IsFinite(t) || t < 0f)
                {
                    continue;
                }
                times.Add(t);
            }
        }

        if (clipEndTime > 0f && float.IsFinite(clipEndTime))
        {
            times.Add(clipEndTime);
        }

        times.Sort();

        var collapsed = new List<float>(times.Count);
        float last = float.NaN;
        for (int i = 0; i < times.Count; i++)
        {
            float t = times[i];
            if (collapsed.Count == 0)
            {
                collapsed.Add(t);
                last = t;
                continue;
            }

            if (MathF.Abs(t - last) <= ANIMATION_TIME_EPSILON)
            {
                collapsed[^1] = t;
                last = t;
                continue;
            }

            collapsed.Add(t);
            last = t;
        }

        if (collapsed.Count < 2)
        {
            float dt = 1f / 30f;
            collapsed = new List<float> { 0f, dt };
        }

        return collapsed.ToArray();
    }

    private static bool IsConstantCurve(IReadOnlyList<Keyframe> keyframes)
    {
        if (keyframes.Count == 0)
        {
            return true;
        }

        float firstValue = keyframes[0].Value;
        for (int i = 1; i < keyframes.Count; i++)
        {
            if (MathF.Abs(keyframes[i].Value - firstValue) > ANIMATION_VALUE_EPSILON)
            {
                return false;
            }

            if (!float.IsFinite(keyframes[i].InTangent) || !float.IsFinite(keyframes[i].OutTangent))
            {
                return false;
            }

            if (MathF.Abs(keyframes[i].InTangent) > ANIMATION_VALUE_EPSILON || MathF.Abs(keyframes[i].OutTangent) > ANIMATION_VALUE_EPSILON)
            {
                return false;
            }
        }

        if (!float.IsFinite(keyframes[0].InTangent) || !float.IsFinite(keyframes[0].OutTangent))
        {
            return false;
        }

        if (MathF.Abs(keyframes[0].InTangent) > ANIMATION_VALUE_EPSILON || MathF.Abs(keyframes[0].OutTangent) > ANIMATION_VALUE_EPSILON)
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<float> SelectSampleTimes(
        bool isConstant,
        int maxKeyCount,
        float clipEndTime,
        IReadOnlyList<float> constantTimes,
        IReadOnlyList<float> bakedTimes,
        params IReadOnlyList<Keyframe>[] channels)
    {
        if (isConstant)
        {
            return constantTimes;
        }
        if (maxKeyCount > bakedTimes.Count)
        {
            return BuildUnionTimes(clipEndTime, channels);
        }
        return bakedTimes;
    }

    private static Dictionary<float, T> ValidateAnimationCurve<T>(Dictionary<float, T> curve, string curveName, string animationName)
    {
        if (curve.Count == 0)
        {
            return curve;
        }

        var validCurve = new Dictionary<float, T>();

        foreach (var kvp in curve.OrderBy(kv => kv.Key))
        {
            float time = kvp.Key;
            if (!float.IsFinite(time) || time < 0 || validCurve.ContainsKey(time))
            {
                continue;
            }

            validCurve[time] = kvp.Value;
        }

        return validCurve;
    }

    private void EnsureMinimumKeyframes<T>(Dictionary<float, T> curve, float defaultEndTime, string bonePath, string trackType)
    {
        if (curve.Count == 1)
        {
            var singleKf = curve.First();
            float endTime = defaultEndTime > singleKf.Key ? defaultEndTime : singleKf.Key + 0.0333f;
            curve[endTime] = singleKf.Value;
            _logger.LogInformation("Duplicated single keyframe for {BonePath}/{TrackType} at t={EndTime:F6}", bonePath, trackType, endTime);
        }
    }

    private static System.Numerics.Quaternion NormalizeSafe(System.Numerics.Quaternion q)
    {
        if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W))
        {
            return System.Numerics.Quaternion.Identity;
        }

        float lenSq = q.LengthSquared();
        if (lenSq <= 0f || !float.IsFinite(lenSq))
        {
            return System.Numerics.Quaternion.Identity;
        }

        return System.Numerics.Quaternion.Normalize(q);
    }

    private sealed class FloatCurveSampler
    {
        private readonly Keyframe[] _keys;
        private int _segmentIndex;

        public FloatCurveSampler(IEnumerable<Keyframe> keyframes)
        {
            _keys = SortAndCollapse(keyframes).ToArray();
            _segmentIndex = 0;
        }

        public float Evaluate(float time)
        {
            if (_keys.Length == 0)
            {
                return 0f;
            }

            if (!float.IsFinite(time))
            {
                return _keys[0].Value;
            }

            if (time <= _keys[0].Time + ANIMATION_TIME_EPSILON)
            {
                return _keys[0].Value;
            }

            if (time >= _keys[^1].Time - ANIMATION_TIME_EPSILON)
            {
                return _keys[^1].Value;
            }

            while (_segmentIndex < _keys.Length - 2 && time > _keys[_segmentIndex + 1].Time + ANIMATION_TIME_EPSILON)
            {
                _segmentIndex++;
            }

            var k0 = _keys[_segmentIndex];
            var k1 = _keys[_segmentIndex + 1];

            if (MathF.Abs(time - k0.Time) <= ANIMATION_TIME_EPSILON)
            {
                return k0.Value;
            }

            if (MathF.Abs(time - k1.Time) <= ANIMATION_TIME_EPSILON)
            {
                return k1.Value;
            }

            float dt = k1.Time - k0.Time;
            if (dt <= ANIMATION_TIME_EPSILON)
            {
                return k0.Value;
            }

            float m0 = k0.OutTangent;
            float m1 = k1.InTangent;

            if (!float.IsFinite(m0) || !float.IsFinite(m1))
            {
                return k0.Value;
            }

            float u = (time - k0.Time) / dt;
            u = Math.Clamp(u, 0f, 1f);

            float u2 = u * u;
            float u3 = u2 * u;

            float h00 = 2f * u3 - 3f * u2 + 1f;
            float h10 = u3 - 2f * u2 + u;
            float h01 = -2f * u3 + 3f * u2;
            float h11 = u3 - u2;

            float value = h00 * k0.Value + h10 * (m0 * dt) + h01 * k1.Value + h11 * (m1 * dt);
            return float.IsFinite(value) ? value : k0.Value;
        }

        private static List<Keyframe> SortAndCollapse(IEnumerable<Keyframe> keyframes)
        {
            var sorted = keyframes
                .Where(k => float.IsFinite(k.Time))
                .OrderBy(k => k.Time)
                .ToList();

            if (sorted.Count <= 1)
            {
                return sorted;
            }

            var collapsed = new List<Keyframe>(sorted.Count);
            var current = sorted[0];

            for (int i = 1; i < sorted.Count; i++)
            {
                var next = sorted[i];
                if (MathF.Abs(next.Time - current.Time) <= ANIMATION_TIME_EPSILON)
                {
                    current = next;
                    continue;
                }

                collapsed.Add(current);
                current = next;
            }

            collapsed.Add(current);
            return collapsed;
        }
    }
}
