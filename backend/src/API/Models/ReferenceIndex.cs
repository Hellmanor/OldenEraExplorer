namespace API.Models;

public enum EntityType
{
    Unit,
    Hero,
    Skill,
    Ability,
    Spell,
    Artifact,
    Building,
    Subclass,
    MapObject,
    Text
}

public readonly record struct EntityRef(
    string EntityId,
    EntityType EntityType,
    string PropertyPath,
    string? DisplayName
);

/// "Used By" queries require reverse lookups. Bidirectional index prevents full scan of all entities.
public sealed class ReferenceIndex
{
    private readonly Dictionary<(string Id, EntityType Type), List<EntityRef>> _references = new();
    private readonly Dictionary<(string Id, EntityType Type), List<EntityRef>> _referencedBy = new();

    public IReadOnlyList<EntityRef> GetReferences(string entityId, EntityType type)
    {
        return _references.TryGetValue((entityId, type), out var refs)
            ? refs
            : Array.Empty<EntityRef>();
    }

    public IReadOnlyList<EntityRef> GetReferencedBy(string entityId, EntityType type)
    {
        return _referencedBy.TryGetValue((entityId, type), out var refs)
            ? refs
            : Array.Empty<EntityRef>();
    }

    public void AddReference(
        string sourceId, EntityType sourceType,
        string targetId, EntityType targetType,
        string propertyPath,
        string? sourceDisplayName = null,
        string? targetDisplayName = null)
    {
        var sourceKey = (sourceId, sourceType);
        if (!_references.ContainsKey(sourceKey))
            _references[sourceKey] = new List<EntityRef>();

        _references[sourceKey].Add(new EntityRef(
            targetId, targetType, propertyPath, targetDisplayName));

        var targetKey = (targetId, targetType);
        if (!_referencedBy.ContainsKey(targetKey))
            _referencedBy[targetKey] = new List<EntityRef>();

        _referencedBy[targetKey].Add(new EntityRef(
            sourceId, sourceType, propertyPath, sourceDisplayName));
    }

    public (int TotalRefs, int TotalEntities) GetStats()
    {
        var totalRefs = _references.Values.Sum(list => list.Count);
        var totalEntities = _references.Keys
            .Concat(_referencedBy.Keys)
            .Distinct()
            .Count();
        return (totalRefs, totalEntities);
    }

    public void Clear()
    {
        _references.Clear();
        _referencedBy.Clear();
    }
}
