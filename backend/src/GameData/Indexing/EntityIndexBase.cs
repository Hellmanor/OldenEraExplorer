using Domain.Contracts;

namespace GameData.Indexing;

public abstract class EntityIndexBase<T> where T : IEntity
{
    protected readonly Dictionary<string, T> _entities = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, T> All => _entities;

    public T? GetById(string id)
        => _entities.TryGetValue(id, out var entity) ? entity : default;

    public bool TryGetById(string id, out T entity)
        => _entities.TryGetValue(id, out entity!);

    public IEnumerable<T> GetAll() => _entities.Values;

    public int Count => _entities.Count;

    protected void Add(T entity) => _entities[entity.Id] = entity;

    protected void AddRange(IEnumerable<T> entities)
    {
        foreach (var entity in entities)
            Add(entity);
    }
}
