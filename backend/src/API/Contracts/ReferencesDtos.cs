namespace API.Contracts;

public record EntityReferenceDto(
    string EntityId,
    string EntityType,
    string? DisplayName,
    string PropertyPath
);

public record EntityReferencesResponse(
    string EntityId,
    string EntityType,
    List<EntityReferenceDto> ReferencedBy
);
