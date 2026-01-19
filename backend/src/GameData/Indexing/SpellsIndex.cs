using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GameData.Indexing;

public sealed class SpellsIndex
{
    public sealed record SpellRecord(
        string Id,
        string NameSid,
        string DescSid,
        string Icon,
        int Rank,
        string School,
        bool IsSpecialMagic = false,
        bool UsedOnMap = false,
        bool HasBattleMagic = false,
        bool HasWorldMagic = false,
        int DealersPerLevelsCount = 0,
        int SettingPerLevelsCount = 0
    );

    private readonly Dictionary<string, SpellRecord> _spells = new();
    public IReadOnlyDictionary<string, SpellRecord> Spells => _spells;

    public void Scan(string streamingAssetsRoot)
    {
        _spells.Clear();
        var zipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(zipPath)) return;

        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e => e.FullName.StartsWith("DB/magics/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && e.Length > 0);

        foreach (var entry in entries)
        {
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var doc = JsonDocument.Parse(reader.ReadToEnd());
                if (!doc.RootElement.TryGetProperty("array", out var array)) continue;

                foreach (var el in array.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";
                    var name = el.TryGetProperty("name", out var nameP) ? nameP.GetString() ?? "" : "";
                    var desc = el.TryGetProperty("description", out var descArr) && descArr.ValueKind == JsonValueKind.Array && descArr.GetArrayLength() > 0
                        ? descArr[0].GetString() ?? "" : "";
                    var icon = el.TryGetProperty("icon", out var iconP) ? iconP.GetString() ?? "" : "";
                    var rank = el.TryGetProperty("rank", out var rankP) && rankP.ValueKind == JsonValueKind.Number ? rankP.GetInt32() : 0;
                    var school = el.TryGetProperty("school_", out var sP) ? sP.GetString() ?? "" : "";

                    var isSpecialMagic = el.TryGetProperty("isSpecialMagic", out var isSM) && isSM.ValueKind == JsonValueKind.True;
                    var usedOnMap = el.TryGetProperty("usedOnMap", out var uom) && uom.ValueKind == JsonValueKind.True;
                    var hasBattleMagic = el.TryGetProperty("battleMagic", out _);
                    var hasWorldMagic = el.TryGetProperty("worldMagic", out _);

                    var dealersCount = 0;
                    if (hasBattleMagic && el.TryGetProperty("battleMagic", out var bm))
                    {
                        if (bm.TryGetProperty("dealersPerLevels", out var dpl) && dpl.ValueKind == JsonValueKind.Array)
                        {
                            dealersCount = dpl.GetArrayLength();
                        }
                    }

                    var settingCount = 0;
                    if (hasWorldMagic && el.TryGetProperty("worldMagic", out var wm))
                    {
                        if (wm.TryGetProperty("settingPerLevels", out var spl) && spl.ValueKind == JsonValueKind.Array)
                        {
                            settingCount = spl.GetArrayLength();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(id) && !_spells.ContainsKey(id))
                        _spells[id] = new SpellRecord(id, name, desc, icon, rank, school, isSpecialMagic, usedOnMap, hasBattleMagic, hasWorldMagic, dealersCount, settingCount);
                }
            }
            catch
            {
                // Continue processing other entries - don't let one bad file stop the scan
            }
        }
    }
}
