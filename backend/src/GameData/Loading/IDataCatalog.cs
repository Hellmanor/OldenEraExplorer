using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameData.Indexing;
using Localization.Indexing;

namespace GameData.Loading;

public interface IDataCatalog : IDisposable
{
    Task<IReadOnlyList<LangIndex.ResolvedEntry>> GetTextsAsync(string locale);
    Task<IReadOnlyList<DbIndex.UnitRecord>> GetUnitsAsync();
    Task<IReadOnlyList<HeroesIndex.HeroRecord>> GetHeroesAsync();
    Task<IReadOnlyList<SkillsIndex.SkillRecord>> GetSkillsAsync();
    Task<IReadOnlyList<SpellsIndex.SpellRecord>> GetSpellsAsync();
    Task<IReadOnlyList<ArtifactsIndex.ArtifactRecord>> GetArtifactsAsync();
    Task<IReadOnlyList<BuildingsIndex.BuildingRecord>> GetBuildingsAsync();
    void SetLocale(string locale);
    Task InvalidateCache();
    Task<bool> IsIndexedAsync();
}
