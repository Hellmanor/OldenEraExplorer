using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameData.Indexing;
using Localization.Indexing;

namespace GameData.Loading;

public sealed class DataCatalog : IDataCatalog
{
    private string _streamingAssetsRoot = "";
    private string _currentLocale = "english";

    private readonly SemaphoreSlim _textsGate = new(1, 1);
    private readonly SemaphoreSlim _unitsGate = new(1, 1);
    private readonly SemaphoreSlim _heroesGate = new(1, 1);
    private readonly SemaphoreSlim _skillsGate = new(1, 1);
    private readonly SemaphoreSlim _spellsGate = new(1, 1);
    private readonly SemaphoreSlim _artifactsGate = new(1, 1);
    private readonly SemaphoreSlim _buildingsGate = new(1, 1);

    private ImmutableArray<LangIndex.ResolvedEntry>? _cachedTexts;
    private ImmutableArray<DbIndex.UnitRecord>? _cachedUnits;
    private ImmutableArray<HeroesIndex.HeroRecord>? _cachedHeroes;
    private ImmutableArray<SkillsIndex.SkillRecord>? _cachedSkills;
    private ImmutableArray<SpellsIndex.SpellRecord>? _cachedSpells;
    private ImmutableArray<ArtifactsIndex.ArtifactRecord>? _cachedArtifacts;
    private ImmutableArray<BuildingsIndex.BuildingRecord>? _cachedBuildings;

    private long _cacheHits;
    private long _cacheMisses;
    private bool _disposed;

    public DataCatalog() { }

    public void SetStreamingAssetsRoot(string path)
    {
        if (path == _streamingAssetsRoot)
            return;

        _streamingAssetsRoot = path;
        InvalidateCacheInternal();
    }

    public void SetLocale(string locale)
    {
        if (locale == _currentLocale)
            return;

        _currentLocale = locale;
        _cachedTexts = null;
    }

    public async Task<IReadOnlyList<LangIndex.ResolvedEntry>> GetTextsAsync(string locale)
    {
        if (locale != _currentLocale)
            SetLocale(locale);

        if (_cachedTexts.HasValue)
        {
            Interlocked.Increment(ref _cacheHits);
            return _cachedTexts.Value;
        }

        await _textsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cachedTexts.HasValue)
            {
                Interlocked.Increment(ref _cacheHits);
                return _cachedTexts.Value;
            }

            Interlocked.Increment(ref _cacheMisses);

            var langIndex = new LangIndex(_streamingAssetsRoot, locale, fallbackToEnglish: true);
            langIndex.Load();

            var entries = langIndex.AllEntries().Select(x => x.Entry).ToImmutableArray();
            _cachedTexts = entries;

            return entries;
        }
        catch
        {
            return ImmutableArray<LangIndex.ResolvedEntry>.Empty;
        }
        finally
        {
            _textsGate.Release();
        }
    }

    public async Task<IReadOnlyList<DbIndex.UnitRecord>> GetUnitsAsync()
    {
        if (_cachedUnits.HasValue)
        {
            Interlocked.Increment(ref _cacheHits);
            return _cachedUnits.Value;
        }

        await _unitsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cachedUnits.HasValue)
            {
                Interlocked.Increment(ref _cacheHits);
                return _cachedUnits.Value;
            }

            Interlocked.Increment(ref _cacheMisses);

            var dbIndex = new DbIndex(_streamingAssetsRoot);
            var units = dbIndex.LoadUnits().ToImmutableArray();
            _cachedUnits = units;

            return units;
        }
        catch
        {
            return ImmutableArray<DbIndex.UnitRecord>.Empty;
        }
        finally
        {
            _unitsGate.Release();
        }
    }

    public async Task<IReadOnlyList<HeroesIndex.HeroRecord>> GetHeroesAsync()
    {
        if (_cachedHeroes.HasValue)
        {
            Interlocked.Increment(ref _cacheHits);
            return _cachedHeroes.Value;
        }

        await _heroesGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cachedHeroes.HasValue)
            {
                Interlocked.Increment(ref _cacheHits);
                return _cachedHeroes.Value;
            }

            Interlocked.Increment(ref _cacheMisses);

            var heroesIndex = new HeroesIndex();
            heroesIndex.ScanHeroes(_streamingAssetsRoot);

            if (_cachedTexts.HasValue)
            {
                var langIndex = new LangIndex(_streamingAssetsRoot, _currentLocale, fallbackToEnglish: true);
                langIndex.Load();
                heroesIndex.SupplementFromLang(langIndex);
            }

            var heroes = heroesIndex.Heroes.Values.ToImmutableArray();
            _cachedHeroes = heroes;

            return heroes;
        }
        catch
        {
            return ImmutableArray<HeroesIndex.HeroRecord>.Empty;
        }
        finally
        {
            _heroesGate.Release();
        }
    }

    public async Task<IReadOnlyList<SkillsIndex.SkillRecord>> GetSkillsAsync()
    {
        if (_cachedSkills.HasValue)
        {
            Interlocked.Increment(ref _cacheHits);
            return _cachedSkills.Value;
        }

        await _skillsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cachedSkills.HasValue)
            {
                Interlocked.Increment(ref _cacheHits);
                return _cachedSkills.Value;
            }

            Interlocked.Increment(ref _cacheMisses);

            var skillsIndex = new SkillsIndex();
            skillsIndex.ScanSkills(_streamingAssetsRoot);

            var skills = skillsIndex.Skills.Values.ToImmutableArray();
            _cachedSkills = skills;

            return skills;
        }
        catch
        {
            return ImmutableArray<SkillsIndex.SkillRecord>.Empty;
        }
        finally
        {
            _skillsGate.Release();
        }
    }

    public async Task<IReadOnlyList<SpellsIndex.SpellRecord>> GetSpellsAsync()
    {
        if (_cachedSpells.HasValue)
        {
            Interlocked.Increment(ref _cacheHits);
            return _cachedSpells.Value;
        }

        await _spellsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cachedSpells.HasValue)
            {
                Interlocked.Increment(ref _cacheHits);
                return _cachedSpells.Value;
            }

            Interlocked.Increment(ref _cacheMisses);

            var spellsIndex = new SpellsIndex();
            spellsIndex.Scan(_streamingAssetsRoot);

            var spells = spellsIndex.Spells.Values.ToImmutableArray();
            _cachedSpells = spells;

            return spells;
        }
        catch
        {
            return ImmutableArray<SpellsIndex.SpellRecord>.Empty;
        }
        finally
        {
            _spellsGate.Release();
        }
    }

    public async Task<IReadOnlyList<ArtifactsIndex.ArtifactRecord>> GetArtifactsAsync()
    {
        if (_cachedArtifacts.HasValue)
        {
            Interlocked.Increment(ref _cacheHits);
            return _cachedArtifacts.Value;
        }

        await _artifactsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cachedArtifacts.HasValue)
            {
                Interlocked.Increment(ref _cacheHits);
                return _cachedArtifacts.Value;
            }

            Interlocked.Increment(ref _cacheMisses);

            var artifactsIndex = new ArtifactsIndex();
            artifactsIndex.Scan(_streamingAssetsRoot);

            var artifacts = artifactsIndex.Artifacts.Values.ToImmutableArray();
            _cachedArtifacts = artifacts;

            return artifacts;
        }
        catch
        {
            return ImmutableArray<ArtifactsIndex.ArtifactRecord>.Empty;
        }
        finally
        {
            _artifactsGate.Release();
        }
    }

    public async Task<IReadOnlyList<BuildingsIndex.BuildingRecord>> GetBuildingsAsync()
    {
        if (_cachedBuildings.HasValue)
        {
            Interlocked.Increment(ref _cacheHits);
            return _cachedBuildings.Value;
        }

        await _buildingsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cachedBuildings.HasValue)
            {
                Interlocked.Increment(ref _cacheHits);
                return _cachedBuildings.Value;
            }

            Interlocked.Increment(ref _cacheMisses);

            var buildingsIndex = new BuildingsIndex();
            buildingsIndex.Scan(_streamingAssetsRoot);

            if (_cachedTexts.HasValue)
            {
                var langIndex = new LangIndex(_streamingAssetsRoot, _currentLocale, fallbackToEnglish: true);
                langIndex.Load();
                buildingsIndex.SupplementFromLang(langIndex);
            }

            var buildings = buildingsIndex.Buildings.Values.ToImmutableArray();
            _cachedBuildings = buildings;

            return buildings;
        }
        catch
        {
            return ImmutableArray<BuildingsIndex.BuildingRecord>.Empty;
        }
        finally
        {
            _buildingsGate.Release();
        }
    }

    public async Task InvalidateCache()
    {
        InvalidateCacheInternal();
        await Task.CompletedTask;
    }

    public async Task<bool> IsIndexedAsync()
    {
        var isIndexed = _cachedTexts.HasValue ||
                        _cachedUnits.HasValue ||
                        _cachedHeroes.HasValue ||
                        _cachedSkills.HasValue ||
                        _cachedSpells.HasValue ||
                        _cachedArtifacts.HasValue ||
                        _cachedBuildings.HasValue;

        await Task.CompletedTask;
        return isIndexed;
    }

    public (long hits, long misses) GetTelemetry()
    {
        return (Interlocked.Read(ref _cacheHits), Interlocked.Read(ref _cacheMisses));
    }

    private void InvalidateCacheInternal()
    {
        _cachedTexts = null;
        _cachedUnits = null;
        _cachedHeroes = null;
        _cachedSkills = null;
        _cachedSpells = null;
        _cachedArtifacts = null;
        _cachedBuildings = null;

        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _textsGate?.Dispose();
        _unitsGate?.Dispose();
        _heroesGate?.Dispose();
        _skillsGate?.Dispose();
        _spellsGate?.Dispose();
        _artifactsGate?.Dispose();
        _buildingsGate?.Dispose();

        _disposed = true;
    }
}
