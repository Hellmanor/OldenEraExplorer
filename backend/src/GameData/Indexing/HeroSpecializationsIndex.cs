using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GameData.Indexing;

public sealed class HeroSpecializationsIndex
{
    public sealed record SpellReplacement(string FromSpellId, string ToSpellId);

    public sealed record SpecializationRecord(
        string Id,
        string NameSid,
        string DescSid,
        string Icon,
        List<SpellReplacement> SpellReplacements);

    private readonly Dictionary<string, SpecializationRecord> _specializations = new();
    public IReadOnlyDictionary<string, SpecializationRecord> Specializations => _specializations;

    public void Scan(string streamingAssetsRoot)
    {
        _specializations.Clear();
        var zipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(zipPath)) return;

        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/heroes_specializations/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            e.Length > 0);

        foreach (var entry in entries)
        {
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = reader.ReadToEnd();

                if (content.Length > 0 && content[0] == '\uFEFF')
                    content = content.Substring(1);

                using var doc = JsonDocument.Parse(content);
                if (!doc.RootElement.TryGetProperty("array", out var array)) continue;

                foreach (var el in array.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";
                    var name = el.TryGetProperty("name", out var nameP) ? nameP.GetString() ?? "" : "";
                    var desc = el.TryGetProperty("desc", out var descP) ? descP.GetString() ?? "" : "";
                    var icon = el.TryGetProperty("icon", out var iconP) ? iconP.GetString() ?? "" : "";

                    var spellReplacements = new List<SpellReplacement>();
                    if (el.TryGetProperty("bonuses", out var bonusesArr) && bonusesArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var bonus in bonusesArr.EnumerateArray())
                        {
                            var bonusType = bonus.TryGetProperty("type", out var typeP) ? typeP.GetString() ?? "" : "";

                            if (bonusType == "heroMagicReplace")
                            {
                                if (bonus.TryGetProperty("parameters", out var paramsArr) &&
                                    paramsArr.ValueKind == JsonValueKind.Array &&
                                    paramsArr.GetArrayLength() >= 2)
                                {
                                    var fromSpell = paramsArr[0].GetString() ?? "";
                                    var toSpell = paramsArr[1].GetString() ?? "";

                                    if (!string.IsNullOrWhiteSpace(fromSpell) && !string.IsNullOrWhiteSpace(toSpell))
                                    {
                                        spellReplacements.Add(new SpellReplacement(fromSpell, toSpell));
                                    }
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(id) && !_specializations.ContainsKey(id))
                    {
                        _specializations[id] = new SpecializationRecord(id, name, desc, icon, spellReplacements);
                    }
                }
            }
            catch
            {
                // Skip files that fail to load
            }
        }
    }

    public List<string> ApplyReplacements(string specializationId, List<string> startMagics)
    {
        if (string.IsNullOrWhiteSpace(specializationId) || !_specializations.TryGetValue(specializationId, out var spec))
        {
            return startMagics;
        }

        if (spec.SpellReplacements.Count == 0)
        {
            return startMagics;
        }

        var result = new List<string>(startMagics.Count);
        foreach (var spellId in startMagics)
        {
            var replacement = spec.SpellReplacements.FirstOrDefault(r =>
                string.Equals(r.FromSpellId, spellId, StringComparison.OrdinalIgnoreCase));

            if (replacement != null)
            {
                result.Add(replacement.ToSpellId);
            }
            else
            {
                result.Add(spellId);
            }
        }

        return result;
    }
}
