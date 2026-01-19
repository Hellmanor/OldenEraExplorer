using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GameData.Indexing;

public sealed class ItemSetsIndex
{
    public sealed record SetBonus(int RequiredItems, string DescSid);

    public sealed record ItemSetRecord(
        string Id,
        string NameSid,
        List<string> ItemIds,
        List<SetBonus> Bonuses
    );

    private readonly Dictionary<string, ItemSetRecord> _itemSets = new();
    private readonly Dictionary<string, string> _descSidToItemSetId = new();

    public IReadOnlyDictionary<string, ItemSetRecord> ItemSets => _itemSets;

    public void Scan(string streamingAssetsRoot)
    {
        _itemSets.Clear();
        _descSidToItemSetId.Clear();
        var zipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(zipPath)) return;

        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e => e.FullName.StartsWith("DB/items/item_sets/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && e.Length > 0);

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

                    var itemIds = new List<string>();
                    if (el.TryGetProperty("itemsInSet", out var itemsInSet) && itemsInSet.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in itemsInSet.EnumerateArray())
                        {
                            var itemId = item.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(itemId))
                                itemIds.Add(itemId);
                        }
                    }

                    var setBonuses = new List<SetBonus>();
                    if (el.TryGetProperty("bonuses", out var bonuses) && bonuses.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var bonus in bonuses.EnumerateArray())
                        {
                            var descSid = bonus.TryGetProperty("desc", out var descP) ? descP.GetString() ?? "" : "";
                            var requiredItemsStr = bonus.TryGetProperty("requiredItemsAmount", out var reqP) ? reqP.GetString() ?? "0" : "0";

                            if (!string.IsNullOrWhiteSpace(descSid) && int.TryParse(requiredItemsStr, out var requiredItems))
                            {
                                setBonuses.Add(new SetBonus(requiredItems, descSid));
                                _descSidToItemSetId[descSid] = id;
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(id) && !_itemSets.ContainsKey(id))
                        _itemSets[id] = new ItemSetRecord(id, name, itemIds, setBonuses);
                }
            }
            catch
            {
                // Continue processing other entries - don't let one bad file stop the scan
            }
        }
    }
}
