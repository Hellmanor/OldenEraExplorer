using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GameData.Indexing;
using Localization.DbAccess;
using Localization.Indexing;
using Localization.Resolution;

namespace GameData.Details;

public class SpellDetailsService
{
    private readonly ILogger<SpellDetailsService> _logger;

    public SpellDetailsService(ILogger<SpellDetailsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SpellDetailsDto> GetDetailsAsync(
        string spellId,
        SpellsIndex.SpellRecord spellRecord,
        string locale,
        DbAccessor dbAccessor,
        LangIndex lang,
        ITextResolver resolver,
        bool placeholderResolverEnabled,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spellId))
            throw new ArgumentNullException(nameof(spellId));
        if (spellRecord == null)
            throw new ArgumentNullException(nameof(spellRecord));
        if (string.IsNullOrWhiteSpace(locale))
            throw new ArgumentNullException(nameof(locale));
        if (dbAccessor == null)
            throw new ArgumentNullException(nameof(dbAccessor));
        if (lang == null)
            throw new ArgumentNullException(nameof(lang));
        if (resolver == null)
            throw new ArgumentNullException(nameof(resolver));

        cancellationToken.ThrowIfCancellationRequested();

        if (!dbAccessor.TryGetMagic(spellId, out var spellJson))
        {
            _logger.LogWarning("Spell JSON not found for spell ID: {SpellId}", spellId);
            return Task.FromResult(CreateEmptyDetails(spellId, spellRecord, lang));
        }

        var descriptionArray = new List<string>();
        if (spellJson.TryGetProperty("description", out var descArr) && descArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in descArr.EnumerateArray())
            {
                descriptionArray.Add(item.GetString() ?? "");
            }
        }

        var bonusDescriptions = new Dictionary<int, string>();
        if (spellJson.TryGetProperty("bonusDescriptions", out var bonusArr) && bonusArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var bonus in bonusArr.EnumerateArray())
            {
                if (bonus.TryGetProperty("level", out var lvl) && bonus.TryGetProperty("description", out var desc))
                {
                    bonusDescriptions[lvl.GetInt32()] = desc.GetString() ?? "";
                }
            }
        }

        string level1Desc = "";
        string level2Desc = "";
        string level2Bonus = "";
        string level3Desc = "";
        string level3Bonus = "";
        string level4Desc = "";
        string level4Bonus = "";

        try
        {
            string desc1 = descriptionArray.Count > 0 ? descriptionArray[0] : "";
            string desc2 = descriptionArray.Count > 1 ? descriptionArray[1] : desc1;
            string desc3 = descriptionArray.Count > 2 ? descriptionArray[2] : desc2;
            string desc4 = descriptionArray.Count > 3 ? descriptionArray[3] : desc3;

            level1Desc = ResolveSpellDescription(spellId, desc1, 1, locale, resolver, lang);
            level2Desc = ResolveSpellDescription(spellId, desc2, 2, locale, resolver, lang);
            level2Bonus = bonusDescriptions.TryGetValue(2, out var b2) ? ResolveSpellDescription(spellId, b2, 2, locale, resolver, lang) : "";
            level3Desc = ResolveSpellDescription(spellId, desc3, 3, locale, resolver, lang);
            level3Bonus = bonusDescriptions.TryGetValue(3, out var b3) ? ResolveSpellDescription(spellId, b3, 3, locale, resolver, lang) : "";
            level4Desc = ResolveSpellDescription(spellId, desc4, 4, locale, resolver, lang);
            level4Bonus = bonusDescriptions.TryGetValue(4, out var b4) ? ResolveSpellDescription(spellId, b4, 4, locale, resolver, lang) : "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during text resolution for spell {SpellId}", spellId);
        }

        string schoolCapitalized = string.IsNullOrEmpty(spellRecord.School) ? "" :
            char.ToUpper(spellRecord.School[0]) + spellRecord.School.Substring(1);
        string schoolTierSid = $"{schoolCapitalized}_magic_{spellRecord.Rank}_tier";
        string schoolTierText = lang.ResolveText(schoolTierSid) ?? "";

        string exceptionText = "";
        if (spellJson.TryGetProperty("excaptionInTooltip", out var exceptionSid) && exceptionSid.ValueKind == JsonValueKind.String)
        {
            var sid = exceptionSid.GetString();
            if (!string.IsNullOrEmpty(sid))
            {
                exceptionText = lang.ResolveText(sid) ?? "";
            }
        }

        int mana1 = 0, mana2 = 0, mana3 = 0, mana4 = 0;
        if (spellJson.TryGetProperty("manaCost", out var manaCostArr) &&
            manaCostArr.ValueKind == JsonValueKind.Array)
        {
            int arrLen = manaCostArr.GetArrayLength();
            if (arrLen > 0 && manaCostArr[0].ValueKind == JsonValueKind.Number)
                mana1 = manaCostArr[0].GetInt32();
            if (arrLen > 1 && manaCostArr[1].ValueKind == JsonValueKind.Number)
                mana2 = manaCostArr[1].GetInt32();
            else
                mana2 = mana1;
            if (arrLen > 2 && manaCostArr[2].ValueKind == JsonValueKind.Number)
                mana3 = manaCostArr[2].GetInt32();
            else
                mana3 = mana2;
            if (arrLen > 3 && manaCostArr[3].ValueKind == JsonValueKind.Number)
                mana4 = manaCostArr[3].GetInt32();
            else
                mana4 = mana3;
        }

        bool isBonusSpell = !spellRecord.IsSpecialMagic
                         && !(spellRecord.UsedOnMap && spellRecord.SettingPerLevelsCount > 1)
                         && !(spellRecord.HasBattleMagic && spellRecord.DealersPerLevelsCount > 1);

        string levelLabelTemplate = lang.ResolveText("tooltipMagicLevelLabel") ?? "Level {0}";
        string manaLabelTemplate = lang.ResolveText("tooltipMagicManaLabel") ?? "Mana: {0}";

        string FormatLabel(string template, object value)
        {
            if (!placeholderResolverEnabled)
                return template;
            return template.Replace("{0}", value.ToString());
        }

        var result = new SpellDetailsDto(
            SpellName: spellRecord.NameSid,
            Icon: spellRecord.Icon,
            Rank: spellRecord.Rank.ToString(),
            School: spellRecord.School,
            SchoolTierText: schoolTierText,
            ExceptionText: exceptionText,
            IsBonusSpell: isBonusSpell,
            Level1Label: FormatLabel(levelLabelTemplate, 1),
            Level2Label: FormatLabel(levelLabelTemplate, 2),
            Level3Label: FormatLabel(levelLabelTemplate, 3),
            Level4Label: FormatLabel(levelLabelTemplate, 4),
            Level1ManaLabel: FormatLabel(manaLabelTemplate, mana1),
            Level2ManaLabel: FormatLabel(manaLabelTemplate, mana2),
            Level3ManaLabel: FormatLabel(manaLabelTemplate, mana3),
            Level4ManaLabel: FormatLabel(manaLabelTemplate, mana4),
            Level1ManaCost: mana1,
            Level2ManaCost: mana2,
            Level3ManaCost: mana3,
            Level4ManaCost: mana4,
            Level1Description: level1Desc,
            Level2Description: level2Desc,
            Level2Bonus: level2Bonus,
            Level3Description: level3Desc,
            Level3Bonus: level3Bonus,
            Level4Description: level4Desc,
            Level4Bonus: level4Bonus
        );

        return Task.FromResult(result);
    }

    private string ResolveSpellDescription(
        string spellId,
        string descSid,
        int level,
        string locale,
        ITextResolver resolver,
        LangIndex lang)
    {
        if (string.IsNullOrWhiteSpace(descSid)) return "";

        var ctx = new ResolutionContext(locale)
        {
            MagicId = spellId,
            MagicLevel = level,
            BuffSpellPower = 0
        };

        var result = resolver.Resolve(descSid, ctx, out _);

        return result;
    }

    private SpellDetailsDto CreateEmptyDetails(string spellId, SpellsIndex.SpellRecord spellRecord, LangIndex lang)
    {
        return new SpellDetailsDto(
            SpellName: spellRecord.NameSid,
            Icon: spellRecord.Icon,
            Rank: spellRecord.Rank.ToString(),
            School: spellRecord.School,
            SchoolTierText: "",
            ExceptionText: "",
            IsBonusSpell: false,
            Level1Label: "",
            Level2Label: "",
            Level3Label: "",
            Level4Label: "",
            Level1ManaLabel: "",
            Level2ManaLabel: "",
            Level3ManaLabel: "",
            Level4ManaLabel: "",
            Level1ManaCost: 0,
            Level2ManaCost: 0,
            Level3ManaCost: 0,
            Level4ManaCost: 0,
            Level1Description: "",
            Level2Description: "",
            Level2Bonus: "",
            Level3Description: "",
            Level3Bonus: "",
            Level4Description: "",
            Level4Bonus: ""
        );
    }
}

public sealed record SpellDetailsDto(
    string SpellName,
    string Icon,
    string Rank,
    string School,
    string SchoolTierText,
    string ExceptionText,
    bool IsBonusSpell,
    string Level1Label,
    string Level2Label,
    string Level3Label,
    string Level4Label,
    string Level1ManaLabel,
    string Level2ManaLabel,
    string Level3ManaLabel,
    string Level4ManaLabel,
    int Level1ManaCost,
    int Level2ManaCost,
    int Level3ManaCost,
    int Level4ManaCost,
    string Level1Description,
    string Level2Description,
    string Level2Bonus,
    string Level3Description,
    string Level3Bonus,
    string Level4Description,
    string Level4Bonus
);
