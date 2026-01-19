using AssetExtractor.Models;

namespace AssetExtractor.Providers;

public interface IPrefabListProvider
{
    PrefabType PrefabType { get; }
    List<PrefabDescriptor> ListPrefabs();
}
