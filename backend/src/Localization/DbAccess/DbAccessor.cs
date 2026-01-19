using System.IO.Compression;
using System.Text.Json;

namespace Localization.DbAccess;

/// <summary>
/// Provides access to game database JSON files from Core.zip.
/// Used by script evaluation for entity data lookups.
/// </summary>
public sealed class DbAccessor : IDisposable
{
    private readonly string _root;
    private ZipArchive? _zip;

    public string StreamingAssetsRoot => _root;

    private readonly Dictionary<string, JsonElement> _unitById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _buffBySidOrId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _heroSpecializationById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _skillById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _subSkillById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _sideBuffById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _abilityById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _obstacleById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _itemById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _itemSetById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _magicById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _trapById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _buildingById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _lawById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _mapObjectById = new(StringComparer.OrdinalIgnoreCase);

    public DbAccessor(string streamingAssetsRoot)
    {
        _root = streamingAssetsRoot ?? throw new ArgumentNullException(nameof(streamingAssetsRoot));
        var zipPath = Path.Combine(_root, "Core.zip");
        _zip = ZipFile.OpenRead(zipPath);
        IndexAll();
    }

    private void IndexAll()
    {
        IndexUnits();
        IndexBuffs();
        IndexHeroSpecializations();
        IndexSkills();
        IndexSubSkills();
        IndexSideBuffs();
        IndexAbilities();
        IndexObstacles();
        IndexItems();
        IndexItemSets();
        IndexMagic();
        IndexTraps();
        IndexBuildings();
        IndexLaws();
        IndexMapObjects();
    }

    private void IndexUnits()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/units/units_logics/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var u in arr.EnumerateArray())
            {
                if (u.ValueKind != JsonValueKind.Object) continue;
                if (!u.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _unitById[id!] = u.Clone();
            }
        }
    }

    private void IndexBuffs()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/buffs/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var b in arr.EnumerateArray())
            {
                if (b.ValueKind != JsonValueKind.Object) continue;
                string? sid = null, id = null;
                if (b.TryGetProperty("sid", out var sidEl) && sidEl.ValueKind == JsonValueKind.String) sid = sidEl.GetString();
                if (b.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String) id = idEl.GetString();
                if (!string.IsNullOrWhiteSpace(sid)) _buffBySidOrId[sid!] = b.Clone();
                if (!string.IsNullOrWhiteSpace(id)) _buffBySidOrId[id!] = b.Clone();
            }
        }
    }

    private void IndexHeroSpecializations()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/heroes_specializations/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var spec in arr.EnumerateArray())
            {
                if (spec.ValueKind != JsonValueKind.Object) continue;
                if (!spec.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _heroSpecializationById[id!] = spec.Clone();
            }
        }
    }

    private void IndexSkills()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/heroes_skills/skills/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var skill in arr.EnumerateArray())
            {
                if (skill.ValueKind != JsonValueKind.Object) continue;
                if (!skill.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _skillById[id!] = skill.Clone();
            }
        }
    }

    private void IndexSubSkills()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/heroes_skills/sub_skills/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var subSkill in arr.EnumerateArray())
            {
                if (subSkill.ValueKind != JsonValueKind.Object) continue;
                if (!subSkill.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _subSkillById[id!] = subSkill.Clone();
            }
        }
    }

    private void IndexSideBuffs()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/logic_side_buffs/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var sideBuff in arr.EnumerateArray())
            {
                if (sideBuff.ValueKind != JsonValueKind.Object) continue;
                if (!sideBuff.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _sideBuffById[id!] = sideBuff.Clone();
            }
        }
    }

    private void IndexAbilities()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/heroes_abilities/heroes_abilities_base/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var ability in arr.EnumerateArray())
            {
                if (ability.ValueKind != JsonValueKind.Object) continue;
                if (!ability.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _abilityById[id!] = ability.Clone();
            }
        }
    }

    private void IndexObstacles()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/field_objects/obstacles/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var obstacle in arr.EnumerateArray())
            {
                if (obstacle.ValueKind != JsonValueKind.Object) continue;
                if (!obstacle.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _obstacleById[id!] = obstacle.Clone();
            }
        }
    }

    private void IndexItems()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/items/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _itemById[id!] = item.Clone();
            }
        }
    }

    private void IndexItemSets()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/items/item_sets/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var itemSet in arr.EnumerateArray())
            {
                if (itemSet.ValueKind != JsonValueKind.Object) continue;
                if (!itemSet.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _itemSetById[id!] = itemSet.Clone();
            }
        }
    }

    private void IndexMagic()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/magics/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var magic in arr.EnumerateArray())
            {
                if (magic.ValueKind != JsonValueKind.Object) continue;
                if (!magic.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _magicById[id!] = magic.Clone();
            }
        }
    }

    private void IndexTraps()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/field_objects/traps/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var trap in arr.EnumerateArray())
            {
                if (trap.ValueKind != JsonValueKind.Object) continue;
                if (!trap.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _trapById[id!] = trap.Clone();
            }
        }
    }

    private void IndexBuildings()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/buildings/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var building in arr.EnumerateArray())
            {
                if (building.ValueKind != JsonValueKind.Object) continue;
                if (!building.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _buildingById[id!] = building.Clone();
            }
        }
    }

    private void IndexLaws()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/fractions_laws/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var law in arr.EnumerateArray())
            {
                if (law.ValueKind != JsonValueKind.Object) continue;
                if (!law.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _lawById[id!] = law.Clone();
            }
        }
    }

    private void IndexMapObjects()
    {
        foreach (var e in _zip!.Entries)
        {
            if (!e.FullName.StartsWith("DB/field_objects/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("array", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var mapObject in arr.EnumerateArray())
            {
                if (mapObject.ValueKind != JsonValueKind.Object) continue;
                if (!mapObject.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                _mapObjectById[id!] = mapObject.Clone();
            }
        }
    }

    public bool TryGetUnit(string unitId, out JsonElement unit) => _unitById.TryGetValue(unitId, out unit);
    public bool TryGetBuff(string sidOrId, out JsonElement buff) => _buffBySidOrId.TryGetValue(sidOrId, out buff);
    public bool TryGetHeroSpecialization(string specializationId, out JsonElement specialization) => _heroSpecializationById.TryGetValue(specializationId, out specialization);
    public bool TryGetSkill(string skillId, out JsonElement skill) => _skillById.TryGetValue(skillId, out skill);
    public bool TryGetSubSkill(string subSkillId, out JsonElement subSkill) => _subSkillById.TryGetValue(subSkillId, out subSkill);
    public bool TryGetAbility(string abilityId, out JsonElement ability) => _abilityById.TryGetValue(abilityId, out ability);
    public bool TryGetHeroAbility(string heroAbilityId, out JsonElement heroAbility) => _abilityById.TryGetValue(heroAbilityId, out heroAbility);
    public bool TryGetObstacle(string obstacleId, out JsonElement obstacle) => _obstacleById.TryGetValue(obstacleId, out obstacle);
    public bool TryGetItem(string itemId, out JsonElement item) => _itemById.TryGetValue(itemId, out item);
    public bool TryGetItemSet(string itemSetId, out JsonElement itemSet) => _itemSetById.TryGetValue(itemSetId, out itemSet);
    public bool TryGetMagic(string magicId, out JsonElement magic) => _magicById.TryGetValue(magicId, out magic);
    public bool TryGetTrap(string trapId, out JsonElement trap) => _trapById.TryGetValue(trapId, out trap);
    public bool TryGetBuilding(string buildingId, out JsonElement building) => _buildingById.TryGetValue(buildingId, out building);
    public bool TryGetLaw(string lawId, out JsonElement law) => _lawById.TryGetValue(lawId, out law);
    public bool TryGetMapObject(string mapObjectId, out JsonElement mapObject) => _mapObjectById.TryGetValue(mapObjectId, out mapObject);

    public bool TryGetSideBuff(string sideBuffId, out JsonElement sideBuff)
    {
        if (_sideBuffById.TryGetValue(sideBuffId, out sideBuff))
            return true;

        if (_buffBySidOrId.TryGetValue(sideBuffId, out sideBuff))
            return true;

        if (sideBuffId.EndsWith("_bonus", StringComparison.OrdinalIgnoreCase))
        {
            var withUnit = sideBuffId.Substring(0, sideBuffId.Length - 6) + "_unit_bonus";
            if (_buffBySidOrId.TryGetValue(withUnit, out sideBuff))
                return true;
        }

        sideBuff = default;
        return false;
    }

    public void Dispose() => _zip?.Dispose();
}
