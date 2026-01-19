#nullable enable
using AssetRipper.SourceGenerated.Classes.ClassID_1;

namespace AssetExtractor.Extraction.Interfaces;

public interface IGameObjectProvider
{
    IGameObject? FindGameObjectByPathId(string sourceFile, long pathId);
    IGameObject? FindPrefabByName(string prefabName);
    AssetLoader Loader { get; }
}
