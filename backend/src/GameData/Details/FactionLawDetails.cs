using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GameData.Indexing;
using Localization.Indexing;
using Localization.Resolution;

namespace GameData.Details;

public sealed record FactionLawLevelDetails(
    int Level,
    int Cost,
    string Description
);

public sealed record FactionLawDetails(
    string LawId,
    string LawName,
    string Icon,
    string FactionId,
    string FactionDisplay,
    string OverviewDescription,
    IReadOnlyList<FactionLawLevelDetails> Levels,
    string PlainTextSummary
);

public sealed class FactionLawDetailsService
{
    private readonly ITextResolver _textResolver;
    private readonly ILogger<FactionLawDetailsService> _logger;

    public FactionLawDetailsService(
        ITextResolver textResolver,
        ILogger<FactionLawDetailsService> logger)
    {
        _textResolver = textResolver ?? throw new ArgumentNullException(nameof(textResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<FactionLawDetails> GetDetailsAsync(
        FactionLawIndex.FactionLawRecord factionLawRecord,
        string locale,
        LangIndex langIndex,
        string factionDisplay,
        CancellationToken cancellationToken = default)
    {
        if (factionLawRecord == null)
            throw new ArgumentNullException(nameof(factionLawRecord));
        if (string.IsNullOrWhiteSpace(locale))
            throw new ArgumentException("Locale cannot be null or empty.", nameof(locale));
        if (langIndex == null)
            throw new ArgumentNullException(nameof(langIndex));
        if (string.IsNullOrWhiteSpace(factionDisplay))
            throw new ArgumentException("Faction display name cannot be null or empty.", nameof(factionDisplay));

        try
        {
            var baseCtx = new ResolutionContext(locale)
            {
                LawId = factionLawRecord.Id
            };

            var lawName = !string.IsNullOrWhiteSpace(factionLawRecord.NameSid)
                ? (_textResolver.Resolve(factionLawRecord.NameSid, baseCtx, out _) ?? langIndex.ResolveText(factionLawRecord.NameSid) ?? factionLawRecord.Id)
                : factionLawRecord.Id;

            var overviewDescription = !string.IsNullOrWhiteSpace(factionLawRecord.DescSid)
                ? (_textResolver.Resolve(factionLawRecord.DescSid, baseCtx, out _) ?? langIndex.ResolveText(factionLawRecord.DescSid) ?? "")
                : "";

            var levels = new List<FactionLawLevelDetails>();
            for (int i = 0; i < factionLawRecord.ParametersPerLevel.Count; i++)
            {
                var levelParams = factionLawRecord.ParametersPerLevel[i];
                int levelNum = i + 1;

                var ctx = new ResolutionContext(locale)
                {
                    LawId = factionLawRecord.Id,
                    LawLevel = levelNum
                };

                string description = "";
                if (!string.IsNullOrWhiteSpace(factionLawRecord.DescSid))
                {
                    description = _textResolver.Resolve(factionLawRecord.DescSid, ctx, out _) ?? "";
                }

                levels.Add(new FactionLawLevelDetails(
                    levelNum,
                    levelParams.Cost,
                    description
                ));
            }

            var plainTextSummary = BuildPlainTextSummary(lawName, overviewDescription, levels, langIndex, locale);

            var details = new FactionLawDetails(
                factionLawRecord.Id,
                lawName,
                factionLawRecord.Icon,
                factionLawRecord.Faction,
                factionDisplay,
                overviewDescription,
                levels,
                plainTextSummary
            );

            return Task.FromResult(details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting faction law details for {LawId}", factionLawRecord.Id);
            throw;
        }
    }

    private string BuildPlainTextSummary(
        string lawName,
        string overviewDescription,
        IReadOnlyList<FactionLawLevelDetails> levels,
        LangIndex langIndex,
        string locale)
    {
        var sb = new StringBuilder();

        sb.AppendLine(lawName);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(overviewDescription))
        {
            sb.AppendLine(overviewDescription);
            sb.AppendLine();
        }

        string levelTemplate = GetLocalizedText(langIndex, "oe_level", "Level {0}");
        string costWord = GetLocalizedText(langIndex, "label_cost", "Cost");

        foreach (var level in levels)
        {
            string levelLabel = levelTemplate.Contains("{0}")
                ? levelTemplate.Replace("{0}", level.Level.ToString())
                : $"Level {level.Level}";
            sb.AppendLine(levelLabel);

            if (!string.IsNullOrWhiteSpace(level.Description))
            {
                sb.AppendLine(level.Description);
            }

            sb.AppendLine($"{costWord}: {level.Cost}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private string GetLocalizedText(LangIndex langIndex, string key, string fallback)
    {
        var text = langIndex.ResolveText(key);
        if (string.IsNullOrWhiteSpace(text) || text == key)
            return fallback;
        return text;
    }
}
