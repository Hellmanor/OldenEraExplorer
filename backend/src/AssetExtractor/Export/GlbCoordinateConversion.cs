#nullable enable
using System.Numerics;

namespace AssetExtractor.Export;

/// <summary>
/// Unity left-handed → glTF right-handed coordinate conversion.
/// Vector3: negate X. Quaternion: negate Y,Z. Tangent: negate X,W.
/// </summary>
public static class GlbCoordinateConversion
{
    public static Vector3 ToGltfVector3Convert(Vector3 v) => new(-v.X, v.Y, v.Z);

    public static Quaternion ToGltfQuaternionConvert(Quaternion q) => new(q.X, -q.Y, -q.Z, q.W);

    public static Vector4 ToGltfTangentConvert(Vector4 t) => new(-t.X, t.Y, t.Z, -t.W);
}
