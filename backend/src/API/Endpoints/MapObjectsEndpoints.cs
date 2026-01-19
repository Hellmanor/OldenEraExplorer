using GameData.Indexing;
using Localization.Resolution;
using API.Contracts;
using API.Services;

namespace API.Endpoints;

public static class MapObjectsEndpoints
{
    public static IEndpointRouteBuilder MapMapObjectsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/map-objects")
            .WithTags("MapObjects")
            ;

        // GET /api/map-objects - List all map objects
        group.MapGet("/", GetMapObjects)
            .WithName("GetMapObjects")
            .WithSummary("List all map objects")
            .WithDescription("Returns a list of all map objects. Supports search filtering by ID or name, and category filtering.")
            .Produces<List<MapObjectListItemDto>>(200)
            .Produces<ErrorDto>(503);

        // GET /api/map-objects/categories - List all unique categories
        group.MapGet("/categories", GetCategories)
            .WithName("GetMapObjectCategories")
            .WithSummary("List all map object categories")
            .WithDescription("Returns a list of all unique map object categories.")
            .Produces<IReadOnlyList<string>>(200)
            .Produces<ErrorDto>(503);

        // GET /api/map-objects/{*id} - Get map object details (catch-all for slash IDs)
        group.MapGet("/{*id}", GetMapObjectById)
            .WithName("GetMapObjectById")
            .WithSummary("Get map object details")
            .WithDescription("Returns detailed information about a specific map object. ID format is category/name (e.g., 'interactive/mine_gold').")
            .Produces<MapObjectDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetMapObjects(
        IGameDataService dataService,
        IGamePathService gamePathService,
        string? search = null,
        string? category = null)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(
                new ErrorDto("Game data not loaded", "Please load game data first using POST /api/game/load"),
                statusCode: 503
            );
        }

        var data = dataService.Data;
        var resolver = data.ResolverFacade;
        var lang = data.Lang;
        var locale = gamePathService.CurrentLocale;

        IEnumerable<MapObjectsIndex.MapObjectRecord> mapObjects = data.MapObjectsIndex.MapObjects.Values;

        // Filter out non-interactable objects
        mapObjects = mapObjects.Where(mo => mo.IsInteractable);

        // Filter out artifacts - they have their own dedicated tab
        mapObjects = mapObjects.Where(mo => !string.Equals(mo.Tag, "Artifact", StringComparison.OrdinalIgnoreCase));

        // Filter using AssetExtractor-consistent logic (matches texture extraction filtering)
        mapObjects = mapObjects.Where(mo => IsAllowedMapObject(mo));

        // Apply category filter
        if (!string.IsNullOrWhiteSpace(category))
        {
            mapObjects = mapObjects.Where(mo => mo.Tag.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            mapObjects = mapObjects.Where(mo =>
                mo.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                GetLocalizedName(resolver, lang, mo, locale)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true
            );
        }

        var mapObjectsList = mapObjects
            .OrderBy(mo => mo.Id)
            .Select(mo => MapToListItem(mo, resolver, lang, locale))
            .ToList();

        return Results.Ok(mapObjectsList);
    }

    private static IResult GetCategories(IGameDataService dataService)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(
                new ErrorDto("Game data not loaded", "Please load game data first using POST /api/game/load"),
                statusCode: 503
            );
        }

        var data = dataService.Data;
        var categories = data.MapObjectsIndex.MapObjects.Values
            .Where(mo => mo.IsInteractable)
            .Where(mo => !string.Equals(mo.Tag, "Artifact", StringComparison.OrdinalIgnoreCase))
            .Where(mo => IsAllowedMapObject(mo))
            .Select(mo => mo.Tag)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        return Results.Ok(categories);
    }

    private static IResult GetMapObjectById(string id, IGameDataService dataService, IGamePathService gamePathService)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(
                new ErrorDto("Game data not loaded", "Please load game data first using POST /api/game/load"),
                statusCode: 503
            );
        }

        var data = dataService.Data;
        var resolver = data.ResolverFacade;
        var lang = data.Lang;
        var locale = gamePathService.CurrentLocale;

        var mapObject = data.MapObjectsIndex.MapObjects.Values
            .FirstOrDefault(mo => mo.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (mapObject is null)
        {
            return Results.NotFound(new ErrorDto(
                $"Map object '{id}' not found",
                "Check the map object ID and try again. Use GET /api/map-objects to list available map objects."
            ));
        }

        var dto = MapToDetail(mapObject, resolver, lang, locale);
        return Results.Ok(dto);
    }

    private static MapObjectListItemDto MapToListItem(
        MapObjectsIndex.MapObjectRecord mapObject,
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string locale)
    {
        var localizedName = GetLocalizedName(resolver, lang, mapObject, locale);
        var icon = BuildIconPath(mapObject.PrefabPath);

        // Use Tag as Category (IDs don't have category prefix like "interactive/mine_gold")
        return new MapObjectListItemDto(
            Id: mapObject.Id,
            Name: localizedName ?? mapObject.Id,
            Category: string.IsNullOrEmpty(mapObject.Tag) ? null : mapObject.Tag,
            Icon: icon
        );
    }

    private static MapObjectDetailDto MapToDetail(
        MapObjectsIndex.MapObjectRecord mapObject,
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string locale)
    {
        var localizedName = GetLocalizedName(resolver, lang, mapObject, locale);
        var description = GetLocalizedDescription(resolver, lang, mapObject, locale);
        var narrativeDescription = GetLocalizedNarrativeDescription(resolver, lang, mapObject, locale);
        var icon = BuildIconPath(mapObject.PrefabPath);

        return new MapObjectDetailDto(
            Id: mapObject.Id,
            Name: localizedName ?? mapObject.Id,
            Description: description,
            NarrativeDescription: narrativeDescription,
            Icon: icon
        );
    }

    private static string? BuildIconPath(string? prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
            return null;

        var iconPath = prefabPath.Replace('\\', '/');
        // Map Objects use Resources/objects/ folder, so prefix with "objects/" if not already present
        if (!iconPath.StartsWith("objects/", StringComparison.OrdinalIgnoreCase))
            iconPath = $"objects/{iconPath}";

        return string.IsNullOrWhiteSpace(iconPath) ? null : iconPath;
    }

    private static string? GetLocalizedName(
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        MapObjectsIndex.MapObjectRecord mapObject,
        string locale)
    {
        // First try the explicit NameSid from JSON
        if (!string.IsNullOrWhiteSpace(mapObject.NameSid))
        {
            var result = TryResolveText(resolver, mapObject.NameSid, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            // Try direct lang lookup
            var langResult = lang.ResolveText(mapObject.NameSid);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        // Fallback to pattern-based SIDs
        var patterns = new[]
        {
            $"{mapObject.Id}_name",
            $"mapobject.{mapObject.Id}.name",
            $"object.{mapObject.Id}.name"
        };

        foreach (var pattern in patterns)
        {
            var result = TryResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(pattern);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        return null;
    }

    private static string? GetLocalizedDescription(
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        MapObjectsIndex.MapObjectRecord mapObject,
        string locale)
    {
        // First try the explicit DescriptionSid from JSON
        if (!string.IsNullOrWhiteSpace(mapObject.DescriptionSid))
        {
            var result = TryResolveText(resolver, mapObject.DescriptionSid, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(mapObject.DescriptionSid);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        // Fallback to pattern-based SIDs
        var patterns = new[]
        {
            $"{mapObject.Id}_description",
            $"mapobject.{mapObject.Id}.description",
            $"object.{mapObject.Id}.description"
        };

        foreach (var pattern in patterns)
        {
            var result = TryResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(pattern);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        return null;
    }

    private static string? GetLocalizedNarrativeDescription(
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        MapObjectsIndex.MapObjectRecord mapObject,
        string locale)
    {
        // First try the explicit NarrativeDescriptionSid from JSON
        if (!string.IsNullOrWhiteSpace(mapObject.NarrativeDescriptionSid))
        {
            var result = TryResolveText(resolver, mapObject.NarrativeDescriptionSid, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(mapObject.NarrativeDescriptionSid);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        // Fallback to pattern-based SIDs
        var patterns = new[]
        {
            $"{mapObject.Id}_narrativeDescription",
            $"mapobject.{mapObject.Id}.narrativeDescription",
            $"object.{mapObject.Id}.narrativeDescription"
        };

        foreach (var pattern in patterns)
        {
            var result = TryResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(pattern);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        return null;
    }

    private static string? TryResolveText(ITextResolver resolver, string sid, string locale)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return null;
        }

        try
        {
            var ctx = new ResolutionContext(locale);
            var result = resolver.Resolve(sid, ctx, out _);

            if (!string.IsNullOrWhiteSpace(result) && result != sid)
            {
                return result;
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool IsAllowedMapObject(MapObjectsIndex.MapObjectRecord mapObject)
    {
        var prefabPath = mapObject.PrefabPath;
        if (string.IsNullOrWhiteSpace(prefabPath))
            return false;

        var lowerPath = prefabPath.ToLowerInvariant().Replace('\\', '/');

        // Stage 1: Only allow prefab paths that start with folders we have extracted textures for
        var hasValidPath = lowerPath.StartsWith("interactive/", StringComparison.Ordinal) ||
                           lowerPath.StartsWith("resource/", StringComparison.Ordinal) ||
                           lowerPath.StartsWith("barracks/", StringComparison.Ordinal);

        if (!hasValidPath)
            return false;

        // Stage 2: Filter out duplicate variants that use the same textures as base objects
        var lowerId = mapObject.Id.ToLowerInvariant();

        // custom_* variants (e.g., custom_storage_wood uses same texture as storage_wood)
        if (lowerId.StartsWith("custom_", StringComparison.Ordinal))
            return false;

        // campaign_* variants (e.g., campaign_fort uses same texture as fort)
        if (lowerId.StartsWith("campaign_", StringComparison.Ordinal))
            return false;

        // *_campaign variants (e.g., fort_campaign uses same texture as fort)
        if (lowerId.EndsWith("_campaign", StringComparison.Ordinal))
            return false;

        // pvp_promo_* variants (use existing barracks textures)
        if (lowerId.StartsWith("pvp_promo_", StringComparison.Ordinal))
            return false;

        // *_old variants (old versions, e.g., learning_stone_old, prison_old)
        if (lowerId.EndsWith("_old", StringComparison.Ordinal))
            return false;

        return true;
    }
}
