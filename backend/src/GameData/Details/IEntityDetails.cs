using Domain.Contracts;
using Localization.Resolution;

namespace GameData.Details;

public interface IEntityDetails<T> where T : IEntity
{
    TDto BuildDetails<TDto>(T entity, ResolutionContext context);
}
