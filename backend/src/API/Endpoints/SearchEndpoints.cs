using API.Contracts;
using API.Services;

namespace API.Endpoints;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/search")
            .WithTags("Search");

        // GET /api/search?q={query} - Global search
        group.MapGet("/", Search)
            .WithName("GlobalSearch")
            .WithSummary("Search across all entities")
            .WithDescription("Searches Units, Heroes, Skills, Spells, Artifacts, Buildings, MapObjects, FactionLaws, Abilities, and Subclasses. Returns all results sorted by relevance.")
            .Produces<SearchResponse>(200)
            .Produces<ErrorDto>(400)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult Search(
        SearchService searchService,
        IGamePathService gamePathService,
        string? q = null)
    {
        var result = searchService.Search(q, gamePathService.CurrentLocale);

        if (result.IsNotReady)
        {
            return Results.Json(
                new ErrorDto("Game data not loaded", result.ErrorMessage ?? ""),
                statusCode: 503
            );
        }

        if (!result.IsSuccess)
        {
            return Results.BadRequest(new ErrorDto(
                "Query too short",
                result.ErrorMessage ?? ""
            ));
        }

        return Results.Ok(new SearchResponse(
            Query: result.Query ?? "",
            Results: result.Results,
            TotalResults: result.TotalResults
        ));
    }
}
