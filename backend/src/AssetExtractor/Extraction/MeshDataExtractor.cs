#nullable enable
using AssetRipper.Numerics;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_23;
using AssetRipper.SourceGenerated.Classes.ClassID_33;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_137;
using AssetRipper.SourceGenerated.Extensions;
using AssetExtractor.Extraction.Interfaces;
using AssetExtractor.Export;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysVector2 = System.Numerics.Vector2;
using SysVector3 = System.Numerics.Vector3;
using SysMatrix4x4 = System.Numerics.Matrix4x4;
using MeshData = AssetExtractor.Models.MeshData;
using BoneWeight = AssetExtractor.Models.BoneWeight;
using Matrix4x4 = AssetExtractor.Models.Matrix4x4;
using SubMeshData = AssetExtractor.Models.SubMeshData;
using HierarchyNode = AssetExtractor.Models.HierarchyNode;
using SkeletonData = AssetExtractor.Models.SkeletonData;
using UnitData = AssetExtractor.Models.UnitData;
using MapObjectData = AssetExtractor.Models.MapObjectData;
using GameObjectData = AssetExtractor.Models.GameObjectData;
using PrefabData = AssetExtractor.Models.PrefabData;

namespace AssetExtractor.Extraction;

// Thread-safety: mesh.ReadData() uses TextureExporter.TextureConversionLock
// because it reads from shared AssetRipper streams (not thread-safe)
public class MeshDataExtractor : IMeshDataExtractor
{
    private readonly ILogger<MeshDataExtractor> _logger;
    private readonly IGameObjectProvider _gameObjectProvider;
    private readonly IBoneDataExtractor _boneDataExtractor;
    private readonly MaterialExtractor _materialExtractor;

    public MeshDataExtractor(
        IGameObjectProvider gameObjectProvider,
        IBoneDataExtractor boneDataExtractor,
        MaterialExtractor materialExtractor,
        ILogger<MeshDataExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<MeshDataExtractor>.Instance;
        _gameObjectProvider = gameObjectProvider;
        _boneDataExtractor = boneDataExtractor;
        _materialExtractor = materialExtractor;
    }

    private static Dictionary<string, int> BuildSkeletonIndexLookup(SkeletonData? skeleton, ILogger? logger = null)
    {
        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        if (skeleton == null) return lookup;

        foreach (var bone in skeleton.Bones)
        {
            if (string.IsNullOrEmpty(bone.Path)) continue;

            if (!lookup.TryAdd(bone.Path, bone.Index))
            {
                (logger ?? NullLogger.Instance).LogWarning(
                    "Duplicate bone path detected: {BonePath} (index {CurrentIndex} vs {ExistingIndex})",
                    bone.Path,
                    bone.Index,
                    lookup[bone.Path]);
            }
        }
        return lookup;
    }

    // Calculate accumulated scale sign from parent hierarchy (-1 or 1) to detect mirroring
    private static Models.Vector3 CalculateAccumulatedScaleSign(HierarchyNode? node)
    {
        float scaleX = 1f, scaleY = 1f, scaleZ = 1f;

        var current = node;
        while (current != null)
        {
            var s = current.LocalTransform.Scale;
            scaleX *= s.X;
            scaleY *= s.Y;
            scaleZ *= s.Z;
            current = current.Parent;
        }

        // Return sign only (-1 or 1)
        return new Models.Vector3(
            scaleX < 0 ? -1f : 1f,
            scaleY < 0 ? -1f : 1f,
            scaleZ < 0 ? -1f : 1f
        );
    }

    public List<MeshData> ExtractMeshesFromInner(
        HierarchyNode inner,
        SkeletonData skeleton,
        UnitData unitData,
        long animatorTransformPathId)
    {
        var meshes = new List<MeshData>();

        var skeletonIndexByPath = BuildSkeletonIndexLookup(skeleton, _logger);

        foreach (var child in inner.Children)
        {
            if (string.Equals(child.Name, "Root", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (HierarchyExtractor.IsVFXNode(child.Name))
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

            var meshAsset = smr.Mesh.TryGetAsset(smr.Collection);
            if (meshAsset is not IMesh mesh)
            {
                _logger.LogWarning("No mesh asset found for child node: {ChildName}", child.Name);
                continue;
            }

            if (!mesh.IsSet())
            {
                _logger.LogWarning("Mesh is not set for child node: {ChildName}", child.Name);
                continue;
            }

            // Thread-safe: lock around mesh.ReadData() - it reads from shared AssetRipper streams
            SysVector3[]? vertices;
            SysVector3[]? normals;
            System.Numerics.Vector4[]? tangents;
            ColorFloat[]? colors;
            SysVector2[]? uv0, uv1, uv2, uv3, uv4, uv5, uv6, uv7;
            BoneWeight4[]? skin;
            SysMatrix4x4[]? bindPose;
            uint[] processedIndexBuffer;

            lock (TextureExporter.TextureConversionLock)
            {
                mesh.ReadData(
                    out vertices,
                    out normals,
                    out tangents,
                    out colors,
                    out uv0, out uv1, out uv2, out uv3,
                    out uv4, out uv5, out uv6, out uv7,
                    out skin,
                    out bindPose,
                    out processedIndexBuffer
                );
            }

            if (vertices == null || vertices.Length == 0)
            {
                _logger.LogWarning("No vertices found for child node: {ChildName}", child.Name);
                continue;
            }

            // Calculate accumulated scale sign from parent hierarchy to detect mirroring
            var accumulatedScale = CalculateAccumulatedScaleSign(child);
            bool hasNegativeScale = accumulatedScale.X < 0 || accumulatedScale.Y < 0 || accumulatedScale.Z < 0;
            if (hasNegativeScale)
            {
                _logger.LogInformation(
                    "Detected negative scale in hierarchy: ({ScaleX}, {ScaleY}, {ScaleZ})",
                    accumulatedScale.X,
                    accumulatedScale.Y,
                    accumulatedScale.Z);
            }

            var meshData = new MeshData
            {
                Name = child.Name,
                LocalTransform = child.LocalTransform,
                AccumulatedScale = accumulatedScale
            };

            meshData.Vertices = ConvertVertices(vertices);
            meshData.Normals = normals != null ? ConvertNormals(normals) : new Models.Vector3[vertices.Length];
            meshData.UV0 = uv0 != null ? ConvertUVs(uv0) : new Models.Vector2[vertices.Length];
            meshData.Tangents = tangents != null ? ConvertTangents(tangents) : Array.Empty<Models.Vector4>();
            meshData.Colors = colors != null && colors.Length > 0 ? ConvertColors(colors) : Array.Empty<Models.Vector4>();
            meshData.Triangles = processedIndexBuffer.Select(i => (int)i).ToArray();

            BoneWeight4[]? effectiveSkin = skin;
            bool skinIsUsable = skin != null && skin.Length > 0;
            if (skinIsUsable)
            {
                int zeroCount = 0;
                int sampleSize = Math.Min(100, skin!.Length);
                for (int i = 0; i < sampleSize; i++)
                {
                    var bw = skin[i];
                    if (bw.Weight0 + bw.Weight1 + bw.Weight2 + bw.Weight3 < 0.0001f)
                    {
                        zeroCount++;
                    }
                }
                if (zeroCount == sampleSize)
                {
                    skinIsUsable = false;
                }
            }

            if (!skinIsUsable && mesh.Skin != null && mesh.Skin.Count > 0)
            {
                effectiveSkin = mesh.Skin.Select(b => b.ToCommonClass()).ToArray();
            }

            meshData.BoneWeights = effectiveSkin != null && effectiveSkin.Length > 0
                ? ConvertBoneWeights(effectiveSkin, child.Name, _logger)
                : CreateDefaultBoneWeights(vertices.Length);

            meshData.BindPoses = bindPose != null
                ? ConvertBindPoses(bindPose)
                : Array.Empty<Matrix4x4>();

            var boneIndices = _boneDataExtractor.MapBoneIndicesToSkeleton(smr, skeletonIndexByPath, animatorTransformPathId);
            meshData.BoneIndices = boneIndices;

            PopulateSubMeshes(meshData, mesh, processedIndexBuffer);

            var materials = _materialExtractor.ExtractMaterials(smr, unitData, _gameObjectProvider.Loader);
            string materialName = "none";
            string textureName = "none";

            if (materials.Count > 0)
            {
                meshData.MaterialName = materials[0].Name;
                materialName = materials[0].Name;
                textureName = materials[0].MainTextureName ?? "none";

                for (int i = 0; i < meshData.SubMeshes.Count && i < materials.Count; i++)
                {
                    meshData.SubMeshes[i].MaterialName = materials[i].Name;
                }
            }

            // Log complete mesh info with material and texture
            _logger.LogInformation(
                "  Mesh: {MeshName} ({VertexCount} vertices, {TriangleCount} triangles) - Material: {MaterialName}, Texture: {TextureName}",
                meshData.Name,
                meshData.Vertices.Length,
                meshData.Triangles.Length / 3,
                materialName,
                textureName);

            meshes.Add(meshData);
        }

        return meshes;
    }

    // Hierarchy paths start from animator node's children to match skeleton convention
    public List<MeshData> ExtractMeshesFromMapObject(HierarchyNode inner, MapObjectData objectData)
    {
        var meshes = new List<MeshData>();

        var skeletonIndexByPath = BuildSkeletonIndexLookup(objectData.Skeleton, _logger);

        // Get animator transform PathID for correct bone path building (same as units)
        long animatorTransformPathId = _boneDataExtractor.GetAnimatorTransformPathId(inner);

        // First check if the inner node itself has a mesh (simple static map objects
        // like abandoned_corpse have the mesh directly on the inner node)
        var innerGO = _gameObjectProvider.FindGameObjectByPathId(inner.SourceFile, inner.PathID);
        if (innerGO != null && inner.IsActive)
        {
            if (innerGO.TryGetComponent<ISkinnedMeshRenderer>(out var smr))
            {
                try
                {
                    var meshAsset = smr.Mesh.TryGetAsset(smr.Collection);
                    if (meshAsset is IMesh mesh && mesh.IsSet())
                    {
                        var meshData = ExtractSkinnedMeshFromIMesh(mesh, inner, smr, objectData, skeletonIndexByPath, animatorTransformPathId);
                        if (meshData != null)
                        {
                            meshData.HierarchyPath = "";
                            meshes.Add(meshData);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to extract inner SMR mesh from node: {NodeName}",
                        inner.Name);
                }
            }
            else if (innerGO.TryGetComponent<IMeshRenderer>(out var mr) &&
                     innerGO.TryGetComponent<IMeshFilter>(out var mf))
            {
                if (!HierarchyExtractor.IsTerrainBlendNode(inner.Name))
                {
                    try
                    {
                        var meshAsset = mf.Mesh.TryGetAsset(mf.Collection);
                        if (meshAsset is IMesh mesh && mesh.IsSet())
                        {
                            var meshData = ExtractMeshFromIMesh(mesh, inner, mr, objectData);
                            if (meshData != null)
                            {
                                meshData.HierarchyPath = "";
                                meshes.Add(meshData);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to extract inner MeshRenderer mesh from node: {NodeName}",
                            inner.Name);
                    }
                }
            }
        }

        // Then process children (for complex map objects with meshes deeper in hierarchy)
        // Paths start from children to match skeleton convention
        foreach (var child in inner.Children)
        {
            ExtractMeshesRecursive(child, "", meshes, objectData, skeletonIndexByPath, animatorTransformPathId);
        }

        // Mark all meshes as map object meshes for winding flip logic
        foreach (var mesh in meshes)
        {
            mesh.IsMapObject = true;
        }

        return meshes;
    }

    public List<MeshData> ExtractMeshesFromGameObject(HierarchyNode node, GameObjectData objectData)
    {
        var meshes = new List<MeshData>();

        var skeletonIndexByPath = BuildSkeletonIndexLookup(objectData.Skeleton, _logger);

        // Get animator transform PathID for correct bone path building
        long animatorTransformPathId = _boneDataExtractor.GetAnimatorTransformPathId(node);

        // Use recursive extraction with skeleton support (like map objects)
        foreach (var child in node.Children)
        {
            ExtractMeshesRecursive(child, "", meshes, objectData, skeletonIndexByPath, animatorTransformPathId);
        }

        return meshes;
    }

    private void ExtractMeshesRecursive(
        HierarchyNode node,
        string parentPath,
        List<MeshData> meshes,
        PrefabData prefabData,
        Dictionary<string, int>? skeletonIndexByPath = null,
        long animatorTransformPathId = 0)
    {
        // Skip inactive nodes and their children
        if (!node.IsActive)
        {
            return;
        }

        // Build the current node's path in the hierarchy
        string currentPath = string.IsNullOrEmpty(parentPath) ? node.Name : $"{parentPath}/{node.Name}";

        var gameObject = _gameObjectProvider.FindGameObjectByPathId(node.SourceFile, node.PathID);
        if (gameObject == null)
        {
            // Still recurse children even if gameObject is null
            foreach (var child in node.Children)
            {
                ExtractMeshesRecursive(child, currentPath, meshes, prefabData, skeletonIndexByPath, animatorTransformPathId);
            }
            return;
        }

        if (gameObject.TryGetComponent<ISkinnedMeshRenderer>(out var smr))
        {
            try
            {
                var meshAsset = smr.Mesh.TryGetAsset(smr.Collection);
                if (meshAsset is IMesh mesh && mesh.IsSet())
                {
                    var meshData = ExtractSkinnedMeshFromIMesh(mesh, node, smr, prefabData, skeletonIndexByPath, animatorTransformPathId);
                    if (meshData != null)
                    {
                        meshData.HierarchyPath = currentPath;
                        meshes.Add(meshData);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to extract SMR mesh from node: {NodeName}",
                    node.Name);
            }
        }
        else if (gameObject.TryGetComponent<IMeshRenderer>(out var meshRenderer))
        {
            // Skip terrain blend meshes (BhCustomTerrain) - they use terrain textures at runtime
            if (HierarchyExtractor.IsTerrainBlendNode(node.Name))
            {
                _logger.LogInformation("Skipping terrain blend mesh: {NodeName}", node.Name);
            }
            else
            {
                try
                {
                    if (gameObject.TryGetComponent<IMeshFilter>(out var meshFilter))
                    {
                        var meshAsset = meshFilter.Mesh.TryGetAsset(meshFilter.Collection);
                        if (meshAsset is IMesh mesh && mesh.IsSet())
                        {
                            var meshData = ExtractMeshFromIMesh(mesh, node, meshRenderer, prefabData);
                            if (meshData != null)
                            {
                                meshData.HierarchyPath = currentPath;
                                meshes.Add(meshData);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to extract MeshRenderer mesh from node: {NodeName}",
                        node.Name);
                }
            }
        }

        foreach (var child in node.Children)
        {
            ExtractMeshesRecursive(child, currentPath, meshes, prefabData, skeletonIndexByPath, animatorTransformPathId);
        }
    }

    private MeshData? ExtractSkinnedMeshFromIMesh(
        IMesh mesh,
        HierarchyNode node,
        ISkinnedMeshRenderer smr,
        PrefabData prefabData,
        Dictionary<string, int>? skeletonIndexByPath,
        long animatorTransformPathId = 0)
    {
        // Thread-safe: lock around mesh.ReadData() - it reads from shared AssetRipper streams
        SysVector3[]? vertices;
        SysVector3[]? normals;
        System.Numerics.Vector4[]? tangents;
        ColorFloat[]? colors;
        SysVector2[]? uv0, uv1, uv2, uv3, uv4, uv5, uv6, uv7;
        BoneWeight4[]? skin;
        SysMatrix4x4[]? bindPoses;
        uint[] processedIndexBuffer;

        lock (TextureExporter.TextureConversionLock)
        {
            mesh.ReadData(
                out vertices,
                out normals,
                out tangents,
                out colors,
                out uv0, out uv1, out uv2, out uv3,
                out uv4, out uv5, out uv6, out uv7,
                out skin,
                out bindPoses,
                out processedIndexBuffer
            );
        }

        if (vertices == null || vertices.Length == 0)
        {
            return null;
        }

        // Calculate accumulated scale sign from parent hierarchy
        var accumulatedScale = CalculateAccumulatedScaleSign(node);

        var meshData = new MeshData
        {
            Name = node.Name,
            LocalTransform = node.LocalTransform,
            AccumulatedScale = accumulatedScale,
            Vertices = ConvertVertices(vertices),
            Normals = normals != null ? ConvertNormals(normals) : new Models.Vector3[vertices.Length],
            UV0 = uv0 != null ? ConvertUVs(uv0) : new Models.Vector2[vertices.Length],
            Tangents = tangents != null ? ConvertTangents(tangents) : Array.Empty<Models.Vector4>(),
            Colors = colors != null && colors.Length > 0 ? ConvertColors(colors) : Array.Empty<Models.Vector4>(),
            Triangles = processedIndexBuffer.Select(i => (int)i).ToArray()
        };

        // Handle skinning data (same as unit extraction)
        BoneWeight4[]? effectiveSkin = skin;
        bool skinIsUsable = skin != null && skin.Length > 0;
        if (skinIsUsable)
        {
            int zeroCount = 0;
            int sampleSize = Math.Min(100, skin!.Length);
            for (int i = 0; i < sampleSize; i++)
            {
                var bw = skin[i];
                if (bw.Weight0 + bw.Weight1 + bw.Weight2 + bw.Weight3 < 0.0001f)
                {
                    zeroCount++;
                }
            }
            if (zeroCount == sampleSize)
            {
                skinIsUsable = false;
            }
        }

        if (!skinIsUsable && mesh.Skin != null && mesh.Skin.Count > 0)
        {
            effectiveSkin = mesh.Skin.Select(b => b.ToCommonClass()).ToArray();
        }

        meshData.BoneWeights = effectiveSkin != null && effectiveSkin.Length > 0
            ? ConvertBoneWeights(effectiveSkin, node.Name, _logger)
            : CreateDefaultBoneWeights(vertices.Length);

        meshData.BindPoses = bindPoses != null
            ? ConvertBindPoses(bindPoses)
            : Array.Empty<Matrix4x4>();

        // Map bone indices to skeleton (like units do)
        if (skeletonIndexByPath != null && skeletonIndexByPath.Count > 0)
        {
            var boneIndices = _boneDataExtractor.MapBoneIndicesToSkeleton(smr, skeletonIndexByPath, animatorTransformPathId);
            meshData.BoneIndices = boneIndices;
            _logger.LogInformation(
                "Skinned mesh extracted: {NodeName} with {BoneIndexCount} bone indices",
                node.Name,
                meshData.BoneIndices.Length);
        }
        else
        {
            meshData.BoneIndices = Array.Empty<int>();
        }

        PopulateSubMeshes(meshData, mesh, processedIndexBuffer);

        var materials = _materialExtractor.ExtractMaterials(smr, prefabData, _gameObjectProvider.Loader);
        if (materials.Count > 0)
        {
            meshData.MaterialName = materials[0].Name;
            for (int i = 0; i < meshData.SubMeshes.Count && i < materials.Count; i++)
            {
                meshData.SubMeshes[i].MaterialName = materials[i].Name;
            }
        }

        return meshData;
    }

    private MeshData? ExtractMeshFromIMesh(IMesh mesh, HierarchyNode node, IMeshRenderer meshRenderer, PrefabData prefabData)
    {
        // Thread-safe: lock around mesh.ReadData() - it reads from shared AssetRipper streams
        SysVector3[]? vertices;
        SysVector3[]? normals;
        System.Numerics.Vector4[]? tangents;
        ColorFloat[]? colors;
        SysVector2[]? uv0, uv1, uv2, uv3, uv4, uv5, uv6, uv7;
        BoneWeight4[]? skin;
        SysMatrix4x4[]? bindPoses;
        uint[] processedIndexBuffer;

        lock (TextureExporter.TextureConversionLock)
        {
            mesh.ReadData(
                out vertices,
                out normals,
                out tangents,
                out colors,
                out uv0, out uv1, out uv2, out uv3,
                out uv4, out uv5, out uv6, out uv7,
                out skin,
                out bindPoses,
                out processedIndexBuffer
            );
        }

        if (vertices == null || vertices.Length == 0)
        {
            return null;
        }

        // Calculate accumulated scale sign from parent hierarchy
        var accumulatedScale = CalculateAccumulatedScaleSign(node);

        var meshData = new MeshData
        {
            Name = node.Name,
            LocalTransform = node.LocalTransform,
            AccumulatedScale = accumulatedScale,
            Vertices = ConvertVertices(vertices),
            Normals = normals != null ? ConvertNormals(normals) : new Models.Vector3[vertices.Length],
            UV0 = uv0 != null ? ConvertUVs(uv0) : new Models.Vector2[vertices.Length],
            Tangents = tangents != null ? ConvertTangents(tangents) : Array.Empty<Models.Vector4>(),
            Colors = colors != null && colors.Length > 0 ? ConvertColors(colors) : Array.Empty<Models.Vector4>(),
            Triangles = processedIndexBuffer.Select(i => (int)i).ToArray(),
            BoneWeights = Array.Empty<BoneWeight>(),
            BoneIndices = Array.Empty<int>(),
            BindPoses = Array.Empty<Matrix4x4>()
        };

        PopulateSubMeshes(meshData, mesh, processedIndexBuffer);

        var materials = _materialExtractor.ExtractMaterials(meshRenderer, prefabData, _gameObjectProvider.Loader);
        if (materials.Count > 0)
        {
            meshData.MaterialName = materials[0].Name;
            for (int i = 0; i < meshData.SubMeshes.Count && i < materials.Count; i++)
            {
                meshData.SubMeshes[i].MaterialName = materials[i].Name;
            }
        }

        return meshData;
    }

    private static Models.Vector3[] ConvertVertices(SysVector3[] vertices)
    {
        var result = new Models.Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            result[i] = new Models.Vector3(vertices[i].X, vertices[i].Y, vertices[i].Z);
        }
        return result;
    }

    private static Models.Vector3[] ConvertNormals(SysVector3[] normals)
    {
        var result = new Models.Vector3[normals.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            var n = normals[i];
            float length = MathF.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
            if (length > 0.0001f)
            {
                result[i] = new Models.Vector3(n.X / length, n.Y / length, n.Z / length);
            }
            else
            {
                result[i] = new Models.Vector3(0, 1, 0);
            }
        }
        return result;
    }

    private static Models.Vector2[] ConvertUVs(SysVector2[] uvs)
    {
        var result = new Models.Vector2[uvs.Length];
        for (int i = 0; i < uvs.Length; i++)
        {
            result[i] = new Models.Vector2(uvs[i].X, uvs[i].Y);
        }
        return result;
    }

    private static Models.Vector4[] ConvertTangents(System.Numerics.Vector4[] tangents)
    {
        var result = new Models.Vector4[tangents.Length];
        for (int i = 0; i < tangents.Length; i++)
        {
            var t = tangents[i];
            result[i] = new Models.Vector4(t.X, t.Y, t.Z, t.W);
        }
        return result;
    }

    private static Models.Vector4[] ConvertColors(ColorFloat[] colors)
    {
        var result = new Models.Vector4[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            result[i] = new Models.Vector4(c.R, c.G, c.B, c.A);
        }
        return result;
    }

    private static BoneWeight[] ConvertBoneWeights(BoneWeight4[] skin, string meshName = "", ILogger? logger = null)
    {
        var result = new BoneWeight[skin.Length];
        int zeroWeightCount = 0;

        for (int i = 0; i < skin.Length; i++)
        {
            var bw = skin[i];
            float weightSum = bw.Weight0 + bw.Weight1 + bw.Weight2 + bw.Weight3;
            bool hasZeroWeights = weightSum < 0.0001f;

            if (hasZeroWeights)
            {
                result[i] = new BoneWeight
                {
                    BoneIndex = { [0] = bw.Index0, [1] = bw.Index1, [2] = bw.Index2, [3] = bw.Index3 },
                    Weight = { [0] = 1.0f, [1] = 0.0f, [2] = 0.0f, [3] = 0.0f }
                };
                zeroWeightCount++;
            }
            else
            {
                result[i] = new BoneWeight
                {
                    BoneIndex = { [0] = bw.Index0, [1] = bw.Index1, [2] = bw.Index2, [3] = bw.Index3 },
                    Weight = { [0] = bw.Weight0, [1] = bw.Weight1, [2] = bw.Weight2, [3] = bw.Weight3 }
                };
            }
        }

        if (zeroWeightCount == skin.Length)
        {
            (logger ?? NullLogger.Instance).LogInformation(
                "{MeshName}: Single-bone attachment (all {VertexCount} vertices use preserved indices)",
                meshName,
                skin.Length);
        }
        return result;
    }

    private static BoneWeight[] CreateDefaultBoneWeights(int vertexCount)
    {
        var result = new BoneWeight[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            result[i] = new BoneWeight();
            result[i].BoneIndex[0] = 0;
            result[i].Weight[0] = 1.0f;
        }
        return result;
    }

    private static Matrix4x4[] ConvertBindPoses(SysMatrix4x4[] bindPoses)
    {
        var result = new Matrix4x4[bindPoses.Length];
        for (int i = 0; i < bindPoses.Length; i++)
        {
            var m = bindPoses[i];
            result[i] = new Matrix4x4(new float[]
            {
                m.M11, m.M21, m.M31, m.M41,
                m.M12, m.M22, m.M32, m.M42,
                m.M13, m.M23, m.M33, m.M43,
                m.M14, m.M24, m.M34, m.M44
            });
        }
        return result;
    }

    private static void PopulateSubMeshes(MeshData meshData, IMesh mesh, uint[] processedIndexBuffer)
    {
        meshData.SubMeshes.Clear();

        var subMeshes = mesh.SubMeshes;
        if (subMeshes.Count == 0)
        {
            return;
        }

        for (int i = 0; i < subMeshes.Count; i++)
        {
            var subMesh = subMeshes[i];

            int indexFormat = mesh.Is16BitIndices() ? 2 : 4;
            int startIndex = (int)(subMesh.FirstByte / (uint)indexFormat);
            int indexCount = (int)subMesh.IndexCount;

            if (indexCount <= 0 || startIndex < 0 || startIndex >= processedIndexBuffer.Length)
            {
                continue;
            }

            int clampedCount = Math.Min(indexCount, processedIndexBuffer.Length - startIndex);
            if (clampedCount <= 0)
            {
                continue;
            }

            var subMeshTris = new int[clampedCount];
            for (int j = 0; j < clampedCount; j++)
            {
                subMeshTris[j] = (int)processedIndexBuffer[startIndex + j];
            }

            uint baseVertex = subMesh.BaseVertex;
            if (baseVertex != 0)
            {
                for (int j = 0; j < subMeshTris.Length; j++)
                {
                    subMeshTris[j] += (int)baseVertex;
                }
            }

            // Get topology from Unity's SubMesh
            var topology = GetMeshTopology(subMesh);

            meshData.SubMeshes.Add(new SubMeshData
            {
                Triangles = subMeshTris,
                MaterialName = string.Empty,
                Topology = topology
            });
        }
    }

    private static Models.MeshTopology GetMeshTopology(AssetRipper.SourceGenerated.Subclasses.SubMesh.ISubMesh subMesh)
    {
        // Use AssetRipper's extension method pattern
        if (subMesh.Has_Topology())
        {
            return (Models.MeshTopology)(int)subMesh.TopologyE;
        }
        else
        {
            // For older Unity versions, IsTriStrip != 0 means TriangleStrip
            return subMesh.IsTriStrip != 0 ? Models.MeshTopology.TriangleStrip : Models.MeshTopology.Triangles;
        }
    }
}
