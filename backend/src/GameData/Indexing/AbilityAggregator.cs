using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace GameData.Indexing;

/// <summary>
/// Key for grouping abilities by their identifying attributes.
/// Abilities with identical NameSid, ResolvedDescription, Type, Rank, and Energy are considered the same ability
/// and will be aggregated together (tracking all source units that have this ability).
/// Uses resolved text for proper variant detection (Avalonia-compatible aggregation logic).
/// </summary>
/// <param name="NameSid">Ability name SID (locale-independent identifier).</param>
/// <param name="ResolvedDescription">Resolved ability description text (used for variant detection - different descriptions create different variants).</param>
/// <param name="Type">Ability type (BaseClass, Passive, Active, Orphan).</param>
/// <param name="Rank">Ability rank/tier (for Active abilities only, empty otherwise).</param>
/// <param name="Energy">Ability energy cost (for Active abilities only, empty otherwise).</param>
public readonly record struct AbilityRowKey(
    string NameSid,
    string ResolvedDescription,
    string Type,
    string Rank,
    string Energy);

/// <summary>
/// Aggregated ability data representing all occurrences of an ability across multiple units (locale-independent).
/// Contains all ability IDs, source unit IDs, and ability SIDs that share the same AbilityRowKey.
/// Resolution to localized text happens on-demand using the current locale.
/// </summary>
/// <param name="VariantId">The unique variant ID (e.g., "jaw_ability_1_name" or "jaw_ability_1_name-var2" for multi-variant abilities).</param>
/// <param name="Key">The identifying attributes for this aggregated ability (uses SIDs).</param>
/// <param name="SourceUnitIds">List of unit IDs that have this ability (ordered).</param>
/// <param name="AbilityIds">List of full ability IDs in format "{unitId}__active_{index}__{nameSid}" or "orphan__{nameSid}" (ordered).</param>
/// <param name="AbilitySids">List of ability name SIDs (ordered, deduplicated).</param>
public record AbilityAggregateResult(
    string VariantId,
    AbilityRowKey Key,
    List<string> SourceUnitIds,
    List<string> AbilityIds,
    List<string> AbilitySids);

/// <summary>
/// Aggregates abilities from multiple sources (units, orphans) into unified groups.
/// Abilities with identical names, types, descriptions, ranks, and energy costs are grouped together,
/// tracking all source units and ability IDs that share these attributes.
/// </summary>
public class AbilityAggregator
{
    private readonly ILogger<AbilityAggregator> _logger;
    private readonly StringComparer _sourceComparer;
    private readonly Dictionary<AbilityRowKey, AbilityAggregateInternal> _aggregates;

    public AbilityAggregator(ILogger<AbilityAggregator> logger, StringComparer? sourceComparer = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sourceComparer = sourceComparer ?? StringComparer.Ordinal;
        _aggregates = new Dictionary<AbilityRowKey, AbilityAggregateInternal>();
    }

    /// <summary>Adds an ability to the aggregation pool. If an ability with the same key already exists, adds to that group.</summary>
    public void AddAbility(
        string abilityId,
        string nameSid,
        string descriptionSid,
        string resolvedDescription,
        string type,
        string sourceUnitId,
        string rank,
        string energy)
    {
        if (string.IsNullOrEmpty(abilityId))
            throw new ArgumentException("Ability ID cannot be null or empty", nameof(abilityId));
        if (string.IsNullOrEmpty(nameSid))
            throw new ArgumentException("Name SID cannot be null or empty", nameof(nameSid));

        var safeResolvedDescription = resolvedDescription ?? string.Empty;
        var safeRank = rank ?? string.Empty;
        var safeEnergy = energy ?? string.Empty;

        // Aggregation key uses resolved description text, not description SID
        // This ensures abilities with different SIDs but identical resolved text are grouped together
        var key = new AbilityRowKey(nameSid, safeResolvedDescription, type, safeRank, safeEnergy);

        if (!_aggregates.TryGetValue(key, out var aggregate))
        {
            aggregate = new AbilityAggregateInternal(key, _sourceComparer);
            _aggregates.Add(key, aggregate);
        }

        aggregate.SourceUnitIds.Add(sourceUnitId);
        aggregate.AbilityIds.Add(abilityId);
        aggregate.AbilitySids.Add(nameSid);
    }

    /// <summary>
    /// Gets all aggregated abilities with variant IDs, sorted by Type then NameSid.
    /// Abilities with the same NameSid but different resolved descriptions get variant suffixes (-var1, -var2, etc.).
    /// Variant ordering is based on source unit IDs (using natural sorting if comparer provided).
    /// Returns locale-independent data; resolution to localized text happens on-demand.
    /// </summary>
    /// <param name="unitIdComparer">Optional comparer for natural sorting of unit IDs (for deterministic variant ordering).</param>
    /// <returns>List of aggregated ability results with all source unit IDs and ability IDs.</returns>
    public List<AbilityAggregateResult> GetAggregates(IComparer<string>? unitIdComparer = null)
    {
        var results = new List<AbilityAggregateResult>();

        var allAggregates = _aggregates.Values
            .OrderBy(a => a.Key.Type, StringComparer.Ordinal)
            .ThenBy(a => a.Key.NameSid, StringComparer.OrdinalIgnoreCase)
            .Select(a => new
            {
                Aggregate = a,
                CombinedSourceIds = a.SourceUnitIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                CombinedIds = a.AbilityIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                CombinedSids = a.AbilitySids.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        var groupedByNameSid = allAggregates
            .GroupBy(a => a.Aggregate.Key.NameSid, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groupedByNameSid)
        {
            var nameSid = group.Key;
            var variants = group.ToList();

            if (variants.Count == 1)
            {
                var variant = variants[0];
                results.Add(new AbilityAggregateResult(
                    nameSid,
                    variant.Aggregate.Key,
                    variant.CombinedSourceIds,
                    variant.CombinedIds,
                    variant.CombinedSids));
            }
            else
            {
                var comparer = unitIdComparer ?? StringComparer.Ordinal;
                var orderedVariants = variants
                    .OrderBy(v => v.CombinedSourceIds.FirstOrDefault() ?? "", comparer)
                    .ToList();

                for (int i = 0; i < orderedVariants.Count; i++)
                {
                    var variant = orderedVariants[i];
                    var variantId = $"{nameSid}-var{i + 1}";

                    results.Add(new AbilityAggregateResult(
                        variantId,
                        variant.Aggregate.Key,
                        variant.CombinedSourceIds,
                        variant.CombinedIds,
                        variant.CombinedSids));
                }
            }
        }

        _logger.LogInformation("Aggregated {AbilityCount} unique abilities from {TotalOccurrences} occurrences",
            results.Count, results.Sum(r => r.AbilityIds.Count));

        return results;
    }

    public void Clear()
    {
        _aggregates.Clear();
    }

    /// <summary>
    /// Internal aggregate storage (mutable for efficient aggregation).
    /// </summary>
    private sealed class AbilityAggregateInternal
    {
        public AbilityAggregateInternal(AbilityRowKey key, IEqualityComparer<string> comparer)
        {
            Key = key;
            SourceUnitIds = new HashSet<string>(comparer);
            AbilityIds = new List<string>();
            AbilitySids = new List<string>();
        }

        public AbilityRowKey Key { get; }
        public HashSet<string> SourceUnitIds { get; }
        public List<string> AbilityIds { get; }
        public List<string> AbilitySids { get; }
    }
}
