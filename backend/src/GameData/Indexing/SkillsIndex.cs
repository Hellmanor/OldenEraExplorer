using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using GameData.Shared.Utils;

namespace GameData.Indexing;

public sealed class SkillsIndex
{
    public sealed record SkillLevelParam(
        string Icon,
        string NameSid,
        string DescSid,
        List<string> SubSkills
    );

    public sealed record SkillRecord(
        string SkillId,
        string NameSid,
        string DescSid,
        string SkillType,
        int MaxLevel,
        List<string> AllSubSkills,
        List<SkillLevelParam> LevelParams,
        string SourceFile,
        bool IsPseudoSkill
    );

    public sealed record SubSkillRecord(
        string SubSkillId,
        string NameSid,
        string DescSid,
        string Icon,
        string SourceFile
    );

    private readonly Dictionary<string, SkillRecord> _skills = new();
    private readonly Dictionary<string, SubSkillRecord> _subSkills = new();

    public IReadOnlyDictionary<string, SkillRecord> Skills => _skills;
    public IReadOnlyDictionary<string, SubSkillRecord> SubSkills => _subSkills;

    public void ScanSkills(string streamingAssetsRoot)
    {
        _skills.Clear();
        _subSkills.Clear();

        var coreZipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(coreZipPath))
            return;

        using var zip = ZipFile.OpenRead(coreZipPath);

        var skillEntries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/heroes_skills/skills/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            e.Length > 0).ToList();

        var subSkillEntries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/heroes_skills/sub_skills/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            e.Length > 0).ToList();

        foreach (var entry in skillEntries)
        {
            try
            {
                ProcessSkillEntry(entry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[SkillsIndex] Error processing {entry.FullName}", ex);
            }
        }

        foreach (var entry in subSkillEntries)
        {
            try
            {
                ProcessSubSkillEntry(entry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[SkillsIndex] Error processing {entry.FullName}", ex);
            }
        }
    }

    private void ProcessSkillEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("array", out var array))
            return;

        foreach (var skillElement in array.EnumerateArray())
        {
            if (!skillElement.TryGetProperty("id", out var idProp))
                continue;

            var skillId = idProp.GetString() ?? "";

            string nameSid = "";
            if (skillElement.TryGetProperty("name", out var nameProp))
                nameSid = nameProp.GetString() ?? "";

            string descSid = "";
            if (skillElement.TryGetProperty("desc", out var descProp))
                descSid = descProp.GetString() ?? "";

            string skillType = "";
            if (skillElement.TryGetProperty("skillType", out var typeProp))
                skillType = typeProp.GetString() ?? "";

            bool isPseudoSkill = false;
            if (skillElement.TryGetProperty("isPseudoSkill", out var isPseudoProp))
                isPseudoSkill = isPseudoProp.GetBoolean();

            var allSubSkills = new HashSet<string>();
            var levelParams = new List<SkillLevelParam>();
            int maxLevel = 0;

            if (skillElement.TryGetProperty("parametersPerLevel", out var paramsArray) &&
                paramsArray.ValueKind == JsonValueKind.Array)
            {
                maxLevel = paramsArray.GetArrayLength();

                foreach (var levelParam in paramsArray.EnumerateArray())
                {
                    var icon = "";
                    if (levelParam.TryGetProperty("icon", out var iconProp))
                        icon = iconProp.GetString() ?? "";

                    var levelNameSid = "";
                    if (levelParam.TryGetProperty("name", out var levelNameProp))
                        levelNameSid = levelNameProp.GetString() ?? "";

                    var levelDescSid = "";
                    if (levelParam.TryGetProperty("desc", out var levelDescProp))
                        levelDescSid = levelDescProp.GetString() ?? "";

                    var subSkillsList = new List<string>();
                    if (levelParam.TryGetProperty("subSkills", out var subSkillsArr) &&
                        subSkillsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var subSkill in subSkillsArr.EnumerateArray())
                        {
                            var subSkillId = subSkill.GetString();
                            if (!string.IsNullOrWhiteSpace(subSkillId))
                            {
                                allSubSkills.Add(subSkillId);
                                subSkillsList.Add(subSkillId);
                            }
                        }
                    }

                    levelParams.Add(new SkillLevelParam(icon, levelNameSid, levelDescSid, subSkillsList));
                }
            }

            if (!_skills.ContainsKey(skillId))
            {
                _skills[skillId] = new SkillRecord(
                    skillId,
                    nameSid,
                    descSid,
                    skillType,
                    maxLevel,
                    allSubSkills.ToList(),
                    levelParams,
                    Path.GetFileName(entry.FullName),
                    isPseudoSkill
                );
            }
        }
    }

    private void ProcessSubSkillEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("array", out var array))
            return;

        foreach (var subSkillElement in array.EnumerateArray())
        {
            if (!subSkillElement.TryGetProperty("id", out var idProp))
                continue;

            var subSkillId = idProp.GetString() ?? "";

            string nameSid = "";
            if (subSkillElement.TryGetProperty("name", out var nameProp))
                nameSid = nameProp.GetString() ?? "";

            string descSid = "";
            if (subSkillElement.TryGetProperty("desc", out var descProp))
                descSid = descProp.GetString() ?? "";

            string icon = "";
            if (subSkillElement.TryGetProperty("icon", out var iconProp))
                icon = iconProp.GetString() ?? "";

            if (!_subSkills.ContainsKey(subSkillId))
            {
                _subSkills[subSkillId] = new SubSkillRecord(
                    subSkillId,
                    nameSid,
                    descSid,
                    icon,
                    Path.GetFileName(entry.FullName)
                );
            }
        }
    }
}
