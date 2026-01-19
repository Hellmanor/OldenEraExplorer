using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using GameData.Shared.Utils;

namespace GameData.Indexing;

/// <summary>
/// Indexes all abilities from Core.zip by scanning three sources:
/// 1. Unit abilities from units_logics/ and units_views/ (merged by index position)
/// 2. Standalone battle abilities from battle_abilities/
/// 3. Hero abilities from heroes_abilities/
/// </summary>
public sealed class AbilityIndex
{
    public sealed record AbilityRecord(
        string AbilityId,
        string AbilityType, // "Passive", "Active", "Alternative", "BaseClass", "BattleAbility", "HeroAbility"
        string? NameSid,
        string? DescriptionSid,
        string? AbilityTypeSid,
        List<string> ImmunitySids,
        List<string> InfoDescriptionSids,
        string SourceUnitId,
        string SourceFile,
        int? Rank,
        int? EnergyCost
    );

    private readonly Dictionary<string, AbilityRecord> _abilities = new();

    public IReadOnlyDictionary<string, AbilityRecord> Abilities => _abilities;

    public void ScanAbilities(string streamingAssetsRoot)
    {
        _abilities.Clear();

        var coreZipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(coreZipPath))
        {
            DiagnosticsLog.Trace($"[AbilityIndex] Core.zip not found at: {coreZipPath}");
            return;
        }

        using var zip = ZipFile.OpenRead(coreZipPath);

        ScanUnitsLogics(zip);
        ScanBattleAbilities(zip);
        ScanHeroesAbilities(zip);
    }

    private void ScanUnitsLogics(ZipArchive zip)
    {
        static string StripSuffix(string name, string suffix)
            => name.EndsWith(suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - suffix.Length)
                : name;

        var viewEntries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/units/units_views/", StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith("_v.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var logicEntriesDict = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/units/units_logics/", StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith("_l.json", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                e => StripSuffix(Path.GetFileNameWithoutExtension(e.FullName), "_l"),
                e => e,
                StringComparer.Ordinal
            );

        foreach (var viewEntry in viewEntries)
        {
            try
            {
                var key = StripSuffix(Path.GetFileNameWithoutExtension(viewEntry.FullName), "_v");
                logicEntriesDict.TryGetValue(key, out var logicEntry);
                ProcessUnitEntryMerged(viewEntry, logicEntry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[AbilityIndex] Error processing {viewEntry.FullName}", ex);
            }
        }
    }

    private void ScanBattleAbilities(ZipArchive zip)
    {
        var battleEntries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/battle_abilities/", StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in battleEntries)
        {
            try
            {
                ProcessBattleAbilityEntry(entry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[AbilityIndex] Error processing {entry.FullName}", ex);
            }
        }
    }

    private void ScanHeroesAbilities(ZipArchive zip)
    {
        var heroEntries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/heroes_abilities/", StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in heroEntries)
        {
            try
            {
                ProcessHeroAbilityEntry(entry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[AbilityIndex] Error processing {entry.FullName}", ex);
            }
        }
    }

    /// <summary>
    /// Merges VIEW data (names, descriptions, icons) with LOGIC data (rank, energy, immunities) by array index.
    /// </summary>
    private void ProcessUnitEntryMerged(ZipArchiveEntry viewEntry, ZipArchiveEntry? logicEntry)
    {
        var viewRoot = ParseJsonBOMTolerant(viewEntry);
        if (viewRoot == null)
            return;

        if (!viewRoot.Value.TryGetProperty("array", out var viewArray))
            return;

        var logicRoot = logicEntry != null ? ParseJsonBOMTolerant(logicEntry) : null;
        JsonElement? logicArray = null;
        if (logicRoot?.TryGetProperty("array", out var la) == true)
            logicArray = la;

        var fileName = Path.GetFileName(viewEntry.FullName);

        int unitIndex = 0;
        foreach (var viewUnit in viewArray.EnumerateArray())
        {
            if (!viewUnit.TryGetProperty("id", out var idProp))
            {
                unitIndex++;
                continue;
            }

            var unitId = idProp.GetString() ?? "";

            JsonElement? logicUnit = null;
            if (logicArray?.ValueKind == JsonValueKind.Array)
            {
                var logicArrayList = logicArray.Value.EnumerateArray().ToList();
                if (unitIndex < logicArrayList.Count)
                    logicUnit = logicArrayList[unitIndex];
            }

            if (viewUnit.TryGetProperty("baseClass", out var baseClassProp))
            {
                JsonElement? baseClassElement = null;

                // Game data uses inconsistent formats: both object and array[0] with single object
                if (baseClassProp.ValueKind == JsonValueKind.Object)
                {
                    baseClassElement = baseClassProp;
                }
                else if (baseClassProp.ValueKind == JsonValueKind.Array)
                {
                    var arr = baseClassProp.EnumerateArray().ToList();
                    if (arr.Count > 0 && arr[0].ValueKind == JsonValueKind.Object)
                        baseClassElement = arr[0];
                }

                if (baseClassElement.HasValue)
                {
                    string? nameSid = null;
                    string? descSid = null;

                    if (baseClassElement.Value.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                        nameSid = nameProp.GetString();
                    if (baseClassElement.Value.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
                        descSid = descProp.GetString();

                    if (!string.IsNullOrWhiteSpace(nameSid))
                    {
                        var abilityId = $"{unitId}__baseclass__{nameSid}";
                        if (!_abilities.ContainsKey(abilityId))
                        {
                            _abilities[abilityId] = new AbilityRecord(
                                abilityId,
                                "BaseClass",
                                nameSid,
                                descSid,
                                null,
                                new List<string>(),
                                new List<string>(),
                                unitId,
                                fileName,
                                null,
                                null
                            );
                        }
                    }
                }
            }

            var viewAbilities = viewUnit.TryGetProperty("abilities", out var va) && va.ValueKind == JsonValueKind.Array
                ? va.EnumerateArray().ToList()
                : new List<JsonElement>();

            var logicAbilities = logicUnit?.TryGetProperty("abilities", out var laa) == true && laa.ValueKind == JsonValueKind.Array
                ? laa.EnumerateArray().ToList()
                : new List<JsonElement>();

            var maxAbilities = Math.Max(viewAbilities.Count, logicAbilities.Count);
            for (int i = 0; i < maxAbilities; i++)
            {
                var vAbility = i < viewAbilities.Count ? viewAbilities[i] : default;
                var lAbility = i < logicAbilities.Count ? logicAbilities[i] : default;
                ExtractAbilityMerged(vAbility, lAbility, unitId, fileName, "Active", i);
            }

            var viewPassives = viewUnit.TryGetProperty("passives", out var vp) && vp.ValueKind == JsonValueKind.Array
                ? vp.EnumerateArray().ToList()
                : new List<JsonElement>();

            var logicPassives = logicUnit?.TryGetProperty("passives", out var lp) == true && lp.ValueKind == JsonValueKind.Array
                ? lp.EnumerateArray().ToList()
                : new List<JsonElement>();

            var maxPassives = Math.Max(viewPassives.Count, logicPassives.Count);
            for (int i = 0; i < maxPassives; i++)
            {
                var vPassive = i < viewPassives.Count ? viewPassives[i] : default;
                var lPassive = i < logicPassives.Count ? logicPassives[i] : default;
                ExtractAbilityMerged(vPassive, lPassive, unitId, fileName, "Passive", i);
            }

            var viewAlts = viewUnit.TryGetProperty("alternativeAttacks", out var valt) && valt.ValueKind == JsonValueKind.Array
                ? valt.EnumerateArray().ToList()
                : new List<JsonElement>();

            var logicAlts = logicUnit?.TryGetProperty("alternativeAttacks", out var lalt) == true && lalt.ValueKind == JsonValueKind.Array
                ? lalt.EnumerateArray().ToList()
                : new List<JsonElement>();

            var maxAlts = Math.Max(viewAlts.Count, logicAlts.Count);
            for (int i = 0; i < maxAlts; i++)
            {
                var vAlt = i < viewAlts.Count ? viewAlts[i] : default;
                var lAlt = i < logicAlts.Count ? logicAlts[i] : default;
                ExtractAbilityMerged(vAlt, lAlt, unitId, fileName, "Alternative", i);
            }

            unitIndex++;
        }
    }

    private void ProcessBattleAbilityEntry(ZipArchiveEntry entry)
    {
        var jsonElement = ParseJsonBOMTolerant(entry);
        if (jsonElement == null)
            return;

        if (!jsonElement.Value.TryGetProperty("array", out var array))
            return;

        var fileName = Path.GetFileName(entry.FullName);

        foreach (var abilityElement in array.EnumerateArray())
        {
            if (!abilityElement.TryGetProperty("id", out var idProp))
                continue;

            var abilityId = idProp.GetString() ?? "";
            ExtractAbility(abilityElement, "(standalone)", fileName, "BattleAbility", 0, abilityId);
        }
    }

    private void ProcessHeroAbilityEntry(ZipArchiveEntry entry)
    {
        var jsonElement = ParseJsonBOMTolerant(entry);
        if (jsonElement == null)
            return;

        if (!jsonElement.Value.TryGetProperty("array", out var array))
            return;

        var fileName = Path.GetFileName(entry.FullName);

        foreach (var abilityElement in array.EnumerateArray())
        {
            if (!abilityElement.TryGetProperty("id", out var idProp))
                continue;

            var abilityId = idProp.GetString() ?? "";
            ExtractAbility(abilityElement, "(hero)", fileName, "HeroAbility", 0, abilityId);
        }
    }

    /// <summary>
    /// Game JSON files may have UTF-8 BOM which System.Text.Json doesn't tolerate by default.
    /// </summary>
    private static JsonElement? ParseJsonBOMTolerant(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var raw = ms.ToArray();

        int offset = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? 3 : 0;
        var text = Encoding.UTF8.GetString(raw, offset, raw.Length - offset);

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private void ExtractAbilityMerged(JsonElement viewAbility, JsonElement logicAbility, string unitId, string fileName, string abilityType, int index)
    {
        string? nameSid = null;
        string? descSid = null;
        string? typeSid = null;
        var immunitySids = new List<string>();
        var infoSids = new List<string>();

        if (viewAbility.ValueKind == JsonValueKind.Object)
        {
            if (viewAbility.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                nameSid = nameProp.GetString();
            if (viewAbility.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
                descSid = descProp.GetString();
            if (viewAbility.TryGetProperty("abilityType", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                typeSid = typeProp.GetString();

            // Game data has typo "excaptionInTooltip" (missing 'e') in some files
            CollectSidsFromViewAbility(viewAbility, "excaptionInTooltip", immunitySids);
            CollectSidsFromViewAbility(viewAbility, "exceptionInTooltip", immunitySids);
            CollectSidsFromViewAbility(viewAbility, "infoDescription", infoSids);
        }

        int? rank = null;
        int? energyCost = null;

        if (logicAbility.ValueKind == JsonValueKind.Object)
        {
            if (logicAbility.TryGetProperty("rank", out var rankProp) && rankProp.ValueKind == JsonValueKind.Number)
                rank = rankProp.GetInt32();

            if (logicAbility.TryGetProperty("energyLevel", out var energyProp) && energyProp.ValueKind == JsonValueKind.Number)
                energyCost = energyProp.GetInt32();
            else if (logicAbility.TryGetProperty("dontUseEnergy", out var dontUseProp) && dontUseProp.ValueKind == JsonValueKind.True)
                energyCost = 0;
        }

        if (string.IsNullOrWhiteSpace(nameSid))
            return;

        var abilityId = $"{unitId}__{nameSid}";

        if (!_abilities.ContainsKey(abilityId))
        {
            _abilities[abilityId] = new AbilityRecord(
                abilityId,
                abilityType,
                nameSid,
                descSid,
                typeSid,
                immunitySids,
                infoSids,
                unitId,
                fileName,
                rank,
                energyCost
            );
        }
    }

    private static void CollectSidsFromViewAbility(JsonElement viewAbility, string propertyName, List<string> target)
    {
        if (!viewAbility.TryGetProperty(propertyName, out var prop))
            return;

        if (prop.ValueKind == JsonValueKind.String)
        {
            var s = prop.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                target.Add(s!);
        }
        else if (prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prop.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        target.Add(s!);
                }
            }
        }
    }

    private void ExtractAbility(JsonElement abilityElement, string unitId, string fileName, string abilityType, int index, string? forcedId = null)
    {
        string? nameSid = null;
        string? descSid = null;
        string? typeSid = null;
        var immunitySids = new List<string>();
        var infoSids = new List<string>();
        int? rank = null;
        int? energyCost = null;

        // Game data uses inconsistent field names across different ability sources
        if (abilityElement.TryGetProperty("nameSid", out var nameProp))
            nameSid = nameProp.GetString();
        else if (abilityElement.TryGetProperty("name", out nameProp))
            nameSid = nameProp.GetString();

        if (abilityElement.TryGetProperty("descriptionSid", out var descProp))
            descSid = descProp.GetString();
        else if (abilityElement.TryGetProperty("descSid", out descProp))
            descSid = descProp.GetString();
        else if (abilityElement.TryGetProperty("description", out descProp))
        {
            if (descProp.ValueKind == JsonValueKind.String)
                descSid = descProp.GetString();
            else if (descProp.ValueKind == JsonValueKind.Array)
            {
                var descList = new List<string>();
                foreach (var d in descProp.EnumerateArray())
                {
                    var dStr = d.GetString();
                    if (!string.IsNullOrWhiteSpace(dStr))
                        descList.Add(dStr);
                }
                if (descList.Count > 0)
                    descSid = descList[0];
            }
        }
        else if (abilityElement.TryGetProperty("desc", out descProp))
            descSid = descProp.GetString();

        if (abilityElement.TryGetProperty("abilityTypeSid", out var typeProp))
            typeSid = typeProp.GetString();

        if (abilityElement.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("immunities", out var immunities) && immunities.ValueKind == JsonValueKind.Array)
            {
                foreach (var imm in immunities.EnumerateArray())
                {
                    if (imm.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tag in tags.EnumerateArray())
                        {
                            var tagStr = tag.GetString();
                            if (!string.IsNullOrWhiteSpace(tagStr))
                                immunitySids.Add(tagStr);
                        }
                    }
                }
            }
        }

        if (abilityElement.TryGetProperty("infoDescriptionSids", out var infoProp) && infoProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var info in infoProp.EnumerateArray())
            {
                var infoStr = info.GetString();
                if (!string.IsNullOrWhiteSpace(infoStr))
                    infoSids.Add(infoStr);
            }
        }

        if (abilityElement.TryGetProperty("rank", out var rankProp) && rankProp.ValueKind == JsonValueKind.Number)
            rank = rankProp.GetInt32();

        if (abilityElement.TryGetProperty("energyLevel", out var energyProp) && energyProp.ValueKind == JsonValueKind.Number)
            energyCost = energyProp.GetInt32();
        else if (abilityElement.TryGetProperty("energyCost", out energyProp) && energyProp.ValueKind == JsonValueKind.Number)
            energyCost = energyProp.GetInt32();
        else if (abilityElement.TryGetProperty("cd", out energyProp) && energyProp.ValueKind == JsonValueKind.Number)
            energyCost = energyProp.GetInt32();

        string abilityId;
        if (!string.IsNullOrWhiteSpace(forcedId))
        {
            abilityId = forcedId;
        }
        else if (!string.IsNullOrWhiteSpace(nameSid))
        {
            abilityId = $"{unitId}__{nameSid}";
        }
        else
        {
            abilityId = $"{unitId}__{abilityType.ToLowerInvariant()}_{index}";
        }

        if (!_abilities.ContainsKey(abilityId))
        {
            _abilities[abilityId] = new AbilityRecord(
                abilityId,
                abilityType,
                nameSid,
                descSid,
                typeSid,
                immunitySids,
                infoSids,
                unitId,
                fileName,
                rank,
                energyCost
            );
        }
    }
}
