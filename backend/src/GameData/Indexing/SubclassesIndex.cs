using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameData.Indexing;

public sealed class SubclassesIndex
{
    public sealed record ActivationCondition(string SkillSid, int SkillLevel, List<string> SubSkillSids);

    public sealed record SubclassRecord(
        string Id,
        string NameSid,
        string DescSid,
        string Icon,
        string Faction,
        string ClassType,
        List<ActivationCondition> RequiredSkills);

    private readonly Dictionary<string, SubclassRecord> _subclasses = new();
    public IReadOnlyDictionary<string, SubclassRecord> Subclasses => _subclasses;

    public void Scan(string streamingAssetsRoot)
    {
        _subclasses.Clear();
        var zipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(zipPath)) return;

        using var zip = ZipFile.OpenRead(zipPath);

        var entries = zip.Entries.Where(e => e.FullName.StartsWith("DB/heroes_sub_classes/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && e.Length > 0);

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
                    var desc = el.TryGetProperty("desc", out var descP) ? descP.GetString() ?? "" : "";
                    var icon = el.TryGetProperty("icon", out var iconP) ? iconP.GetString() ?? "" : "";
                    var faction = el.TryGetProperty("faction", out var fP) ? fP.GetString() ?? "" : "";
                    var classType = el.TryGetProperty("classType", out var cP) ? cP.GetString() ?? "" : "";

                    var requiredSkills = new List<ActivationCondition>();
                    if (el.TryGetProperty("activationConditions", out var acArr) && acArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var condition in acArr.EnumerateArray())
                        {
                            var skillSid = condition.TryGetProperty("skillSid", out var sSid) ? sSid.GetString() ?? "" : "";
                            var skillLevel = condition.TryGetProperty("skillLevel", out var sLvl) ? sLvl.GetInt32() : 0;
                            var subSkillSids = new List<string>();

                            if (condition.TryGetProperty("subSkillSids", out var ssArr) && ssArr.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var ss in ssArr.EnumerateArray())
                                {
                                    var subSkillSid = ss.GetString();
                                    if (!string.IsNullOrWhiteSpace(subSkillSid))
                                        subSkillSids.Add(subSkillSid);
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(skillSid))
                                requiredSkills.Add(new ActivationCondition(skillSid, skillLevel, subSkillSids));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(id) && !_subclasses.ContainsKey(id))
                        _subclasses[id] = new SubclassRecord(id, name, desc, icon, faction, classType, requiredSkills);
                }
            }
            catch
            {
                // Continue processing other entries - don't let one bad file stop the scan
            }
        }

        // Supplement from Lang for factions without DB data (e.g., nature)
        SupplementFromLang(streamingAssetsRoot);
    }

    /// <summary>
    /// Supplement subclasses with placeholders from Lang files for factions that don't have DB data.
    /// This allows displaying subclasses like sub_class_nature_might_1 even if DB/heroes_sub_classes/sub_classes_nature.json doesn't exist.
    /// </summary>
    private void SupplementFromLang(string streamingAssetsRoot)
    {
        // Pattern: sub_class_{faction}_{classType}_{number}_name
        // Example: sub_class_nature_might_1_name
        var subclassNamePattern = new Regex(@"^sub_class_([a-z]+)_(might|magic)_(\d+)_name$", RegexOptions.IgnoreCase);

        try
        {
            // Lang files are in StreamingAssets/Lang, not in Core.zip
            var langFilePath = Path.Combine(streamingAssetsRoot, "Lang", "english", "texts", "heroSkills.json");
            if (!File.Exists(langFilePath)) return;

            string jsonContent = File.ReadAllText(langFilePath, Encoding.UTF8);
            using var doc = JsonDocument.Parse(jsonContent);

            if (!doc.RootElement.TryGetProperty("tokens", out var texts)) return;

            var synthesizedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var textEntry in texts.EnumerateArray())
            {
                if (!textEntry.TryGetProperty("sid", out var sidProp)) continue;
                var sid = sidProp.GetString();
                if (string.IsNullOrWhiteSpace(sid)) continue;

                var match = subclassNamePattern.Match(sid);
                if (!match.Success) continue;

                var faction = match.Groups[1].Value;
                var classType = match.Groups[2].Value;
                var number = match.Groups[3].Value;
                var subclassId = $"sub_class_{faction}_{classType}_{number}";

                if (_subclasses.ContainsKey(subclassId)) continue;
                if (synthesizedIds.Contains(subclassId)) continue;

                synthesizedIds.Add(subclassId);

                // SID pattern may differ from JSON (e.g., "demons" vs "demon")
                var nameSid = sid;
                var descSid = $"sub_class_{faction}_{classType}_{number}_desc";
                var icon = $"sub_class_{faction}_{classType}_{number}_icon";

                var syntheticRecord = new SubclassRecord(
                    Id: subclassId,
                    NameSid: nameSid,
                    DescSid: descSid,
                    Icon: icon,
                    Faction: faction,
                    ClassType: classType,
                    RequiredSkills: new List<ActivationCondition>() // No activation conditions available
                );

                _subclasses[subclassId] = syntheticRecord;
            }
        }
        catch
        {
            // If synthesis fails, continue with whatever was loaded from JSON
        }
    }
}
