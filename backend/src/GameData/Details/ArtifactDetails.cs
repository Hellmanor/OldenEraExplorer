using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GameData.Indexing;
using Localization.DbAccess;
using Localization.Indexing;
using Localization.Resolution;
using Localization.Services;

namespace GameData.Details;

public class ArtifactDetailsService
{
    private readonly DbAccessor _dbAccessor;
    private readonly ITextResolver _textResolver;
    private readonly ILogger<ArtifactDetailsService> _logger;

    public ArtifactDetailsService(
        DbAccessor dbAccessor,
        ITextResolver textResolver,
        ILogger<ArtifactDetailsService> logger)
    {
        _dbAccessor = dbAccessor ?? throw new ArgumentNullException(nameof(dbAccessor));
        _textResolver = textResolver ?? throw new ArgumentNullException(nameof(textResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ArtifactDetailsViewModel> GetDetailsAsync(
        string artifactId,
        string streamingAssetsRoot,
        string locale,
        LangIndex lang,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artifactId))
            throw new ArgumentException("Artifact ID cannot be null or whitespace.", nameof(artifactId));
        if (string.IsNullOrWhiteSpace(locale))
            throw new ArgumentException("Locale cannot be null or whitespace.", nameof(locale));
        if (lang == null)
            throw new ArgumentNullException(nameof(lang));

        cancellationToken.ThrowIfCancellationRequested();

        var artifactsIndex = new ArtifactsIndex();
        artifactsIndex.Scan(streamingAssetsRoot);

        if (!artifactsIndex.Artifacts.TryGetValue(artifactId, out var artifact))
        {
            _logger.LogWarning("Artifact {ArtifactId} not found in index", artifactId);
            throw new InvalidOperationException($"Artifact '{artifactId}' not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var ctx = new ResolutionContext(locale)
        {
            ItemId = artifactId,
            ItemLevel = 1
        };

        var name = lang.ResolveText(artifact.NameSid) ?? artifactId;
        var description = _textResolver.Resolve(artifact.DescSid, ctx, out _) ?? lang.ResolveText(artifact.DescSid) ?? "";

        var rarityLower = artifact.Rarity.ToLowerInvariant();
        var slotNormalized = NormalizeSlotForSid(artifact.Slot);
        var combinedSid = $"artifactRarity_{rarityLower}_{slotNormalized}";
        var raritySlotText = lang.ResolveText(combinedSid);

        if (raritySlotText == null && slotNormalized == "ARMOR")
        {
            var combinedSidBritish = $"artifactRarity_{rarityLower}_ARMOUR";
            raritySlotText = lang.ResolveText(combinedSidBritish);
        }

        raritySlotText ??= $"{artifact.Rarity} {artifact.Slot}";

        var narrativeDesc = "";
        if (!string.IsNullOrWhiteSpace(artifact.NarrativeDescSid))
            narrativeDesc = _textResolver.Resolve(artifact.NarrativeDescSid, ctx, out _) ?? lang.ResolveText(artifact.NarrativeDescSid) ?? "";

        var upgradeDesc = "";
        if (!string.IsNullOrWhiteSpace(artifact.UpgradeDescSid))
            upgradeDesc = _textResolver.Resolve(artifact.UpgradeDescSid, ctx, out _) ?? lang.ResolveText(artifact.UpgradeDescSid) ?? "";

        cancellationToken.ThrowIfCancellationRequested();

        var upgradeCost = BuildUpgradeCost(artifact, lang, locale);

        SetBonus? setBonus = null;
        if (!string.IsNullOrWhiteSpace(artifact.ItemSetId))
        {
            setBonus = await BuildSetBonusAsync(artifact.ItemSetId, streamingAssetsRoot, locale, lang, artifactsIndex, cancellationToken);
        }

        var slotIcon = MapSlotToIcon(artifact.Slot);

        return await Task.FromResult(new ArtifactDetailsViewModel(
            name,
            artifact.Icon,
            raritySlotText,
            artifact.Rarity,
            slotIcon,
            description,
            narrativeDesc,
            upgradeDesc,
            upgradeCost.CostTextView,
            upgradeCost.CostTextMode,
            upgradeCost.CostNote,
            setBonus));
    }

    private async Task<SetBonus?> BuildSetBonusAsync(
        string itemSetId,
        string streamingAssetsRoot,
        string locale,
        LangIndex lang,
        ArtifactsIndex artifactsIndex,
        CancellationToken cancellationToken)
    {
        var itemSetsIndex = new ItemSetsIndex();
        itemSetsIndex.Scan(streamingAssetsRoot);

        if (!itemSetsIndex.ItemSets.TryGetValue(itemSetId, out var itemSet))
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        var setName = lang.ResolveText(itemSet.NameSid) ?? itemSet.Id;

        var setBonusCtx = new ResolutionContext(locale)
        {
            ItemSetId = itemSet.Id,
            ItemLevel = 1
        };

        var setBonusTemplate = lang.ResolveText("tooltipItemEffectsSet") ?? "When having {0} items:";

        var bonuses = new List<SetBonusEntry>();
        foreach (var bonus in itemSet.Bonuses)
        {
            var bonusDesc = _textResolver.Resolve(bonus.DescSid, setBonusCtx, out _) ?? lang.ResolveText(bonus.DescSid) ?? "";
            var header = string.Format(setBonusTemplate, bonus.RequiredItems);
            var formattedText = $"{header}\n{bonusDesc}";

            bonuses.Add(new SetBonusEntry(bonus.RequiredItems, header, bonusDesc, formattedText));
        }

        var setItems = new List<SetItemEntry>();
        foreach (var itemId in itemSet.ItemIds)
        {
            if (artifactsIndex.Artifacts.TryGetValue(itemId, out var art))
            {
                var itemName = lang.ResolveText(art.NameSid) ?? itemId;
                var itemSlotIcon = MapSlotToIcon(art.Slot);

                setItems.Add(new SetItemEntry(
                    itemId,
                    itemName,
                    itemSlotIcon,
                    art.Rarity,
                    art.Icon,
                    art.Slot));
            }
        }

        return await Task.FromResult(new SetBonus(setName, bonuses, setItems));
    }

    private (string CostTextView, string CostTextMode, string CostNote) BuildUpgradeCost(
        ArtifactsIndex.ArtifactRecord artifact,
        LangIndex lang,
        string locale)
    {
        if (artifact.MaxLevel <= 1)
            return ("", "", "");

        // Get localized label: try overlay first, then game lang files
        var costLabel = OverlayService.Instance.TryResolveFromOverlay("label_upgrade_cost", locale)
                     ?? lang.ResolveText("label_upgrade_cost")
                     ?? "Upgrade Cost: {0}";
        var costTextView = string.Format(costLabel, artifact.CostBase);
        var dustName = lang.ResolveText("dust_name") ?? "dust";
        var costWithCurrency = $"{artifact.CostBase} {dustName}";
        var costTextMode = string.Format(costLabel, costWithCurrency);

        var costNote = "";
        if (artifact.MaxLevel == 999)
        {
            costNote = lang.ResolveText("tooltipMagicAfter") ?? "";
        }

        return (costTextView, costTextMode, costNote);
    }

    private static string NormalizeSlotForSid(string slot)
    {
        return slot.ToLowerInvariant().Replace(" ", "_") switch
        {
            "armour" or "armor" => "ARMOR",
            "back" => "BACK",
            "belt" => "BELT",
            "boots" => "BOOTS",
            "head" => "HEAD",
            "left_hand" or "main_hand" => "LEFT_HAND",
            "right_hand" or "off_hand" => "RIGHT_HAND",
            "ring" => "RING",
            "unique_slot" or "unic_slot" => "UNIQUE_SLOT",
            _ => slot.ToUpperInvariant().Replace(" ", "_")
        };
    }

    private static string MapSlotToIcon(string slot)
    {
        var normalized = slot.ToLowerInvariant().Replace(" ", "_");
        var result = normalized switch
        {
            "armour" or "armor" => "armor",
            "back" or "cape" or "cloak" or "shoulder" or "shoulders" => "back",
            "belt" or "waist" => "belt",
            "boots" or "feet" or "foot" => "boots",
            "head" or "helm" or "helmet" => "head",
            "left_hand" or "main_hand" or "weapon" or "mainhand" => "left_hand",
            "right_hand" or "off_hand" or "shield" or "offhand" => "right_hand",
            "ring" or "finger" => "ring",
            "unique_slot" or "unic_slot" or "trinket" or "relic" or "accessory" => "unique_slot",
            "item" or "item_slot" or "misc" or "consumable" => "item_slot",
            _ => "item_slot"
        };

        return result;
    }
}

public record ArtifactDetailsViewModel(
    string Name,
    string Icon,
    string RaritySlotText,
    string RarityName,
    string SlotIcon,
    string Description,
    string NarrativeDescription,
    string UpgradeDescription,
    string UpgradeCostTextView,
    string UpgradeCostTextMode,
    string UpgradeCostNote,
    SetBonus? SetBonus);

public record SetBonus(
    string SetName,
    List<SetBonusEntry> Bonuses,
    List<SetItemEntry> SetItems);

public record SetBonusEntry(
    int PieceCount,
    string Header,
    string Effect,
    string FormattedText);

public record SetItemEntry(
    string ArtifactId,
    string Name,
    string SlotIcon,
    string RarityName,
    string Icon,
    string Slot);
