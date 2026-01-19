#nullable enable
namespace AssetExtractor.Models;

public abstract class PrefabData
{
    public string Name { get; set; } = string.Empty;
    public PrefabType Type { get; protected set; }
    public string PrefabGuid { get; set; } = string.Empty;
    public HierarchyNode? RootNode { get; set; }
    public HierarchyNode? WrapperNode { get; set; }
    public HierarchyNode? InnerNode { get; set; }
    public List<MeshData> Meshes { get; set; } = new();
    public List<MaterialData> Materials { get; set; } = new();
    public List<TextureData> Textures { get; set; } = new();
    /// <summary>
    /// Position of the look_point node in Unity coordinates (if present).
    /// When non-zero, the exporter will rotate the model so this direction faces glTF Z+.
    /// </summary>
    public Vector3? LookPointPosition { get; set; }

    protected PrefabData()
    {
    }
}
