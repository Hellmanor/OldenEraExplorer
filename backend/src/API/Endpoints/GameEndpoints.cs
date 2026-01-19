using API.Contracts;
using Localization.Services;
using API.Services;

namespace API.Endpoints;

public static class GameEndpoints
{
    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/game")
            .WithTags("Game")
            ;

        // GET /api/game/detect - Auto-detect game installations
        group.MapGet("/detect", DetectGame)
            .WithName("DetectGame")
            .WithSummary("Auto-detect game installations")
            .WithDescription("Scans the system for Heroes Olden Era game installations and returns all candidates with confidence scores.")
            .Produces<DetectionResultDto>(200);

        // POST /api/game/path - Set game path manually
        group.MapPost("/path", SetGamePath)
            .WithName("SetGamePath")
            .WithSummary("Set game path manually")
            .WithDescription("Manually configure the game installation path and optional locale.")
            .Produces<SetPathResultDto>(200)
            .Produces<ErrorDto>(400);

        // GET /api/game/status - Get current game status
        group.MapGet("/status", GetGameStatus)
            .WithName("GetGameStatus")
            .WithSummary("Get current game status")
            .WithDescription("Returns the current state of game path configuration and data loading.")
            .Produces<GameStatusDto>(200);

        // POST /api/game/load - Trigger data load
        group.MapPost("/load", LoadGameData)
            .WithName("LoadGameData")
            .WithSummary("Load game data")
            .WithDescription("Triggers loading of game data from the configured path. Requires a valid game path to be set first.")
            .Produces<GameStatusDto>(200)
            .Produces<ErrorDto>(400)
            .Produces<ErrorDto>(503);

        // DELETE /api/game/path - Clear game path configuration
        group.MapDelete("/path", ClearGamePath)
            .WithName("ClearGamePath")
            .WithSummary("Clear game path configuration")
            .WithDescription("Clears the current game path configuration and unloads game data.");

        return endpoints;
    }

    private static IResult DetectGame(IGamePathService pathService)
    {
        // REMOVE THIS comment to test "No game data" state
        // return Results.Ok(new DetectionResultDto(false, null, null, null, []));

        var result = pathService.AutoDetect();

        var dto = new DetectionResultDto(
            Success: result.Success,
            SelectedGameRoot: result.SelectedGameRoot,
            HeroesOeDataPath: result.HeroesOeDataPath,
            StreamingAssetsPath: result.StreamingAssetsPath,
            Candidates: result.AllCandidates
                .Select(c => new CandidateDto(
                    GameRoot: c.GameRoot,
                    HeroesOeDataPath: c.HeroesOeDataPath,
                    StreamingAssetsPath: c.StreamingAssetsPath,
                    Score: c.Score,
                    DisplayName: c.DisplayName
                ))
                .ToList()
        );

        return Results.Ok(dto);
    }

    private static IResult SetGamePath(SetPathRequest request, IGamePathService pathService)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return Results.BadRequest(new ErrorDto("Path is required", "The 'path' field cannot be empty."));
        }

        var result = pathService.SetPath(request.Path, request.Locale);

        var dto = new SetPathResultDto(
            Success: result.Success,
            Error: result.Error,
            GameRoot: result.GameRoot,
            HeroesOeDataPath: result.HeroesOeDataPath,
            StreamingAssetsPath: result.StreamingAssetsPath
        );

        if (!result.Success)
        {
            return Results.BadRequest(dto);
        }

        return Results.Ok(dto);
    }

    private static IResult GetGameStatus(IGamePathService pathService, IGameDataService dataService)
    {
        var dto = new GameStatusDto(
            PathSet: pathService.IsPathSet,
            DataLoaded: dataService.IsLoaded,
            GameRoot: pathService.GameRoot,
            HeroesOeDataPath: pathService.HeroesOeDataPath,
            StreamingAssetsPath: pathService.StreamingAssetsPath,
            CurrentLocale: pathService.CurrentLocale,
            Error: dataService.Error,
            UiLabels: BuildUiLabels(pathService.CurrentLocale)
        );

        return Results.Ok(dto);
    }

    private static Dictionary<string, string> BuildUiLabels(string locale)
    {
        var overlay = OverlayService.Instance;
        var result = new Dictionary<string, string>();

        var englishOverlay = overlay.EnglishOverlay;
        var localeOverlay = overlay.GetOverlayForLocale(locale);

        foreach (var key in englishOverlay.Keys)
        {
            // Try locale first, then english, then raw key
            if (localeOverlay != null && localeOverlay.TryGetValue(key, out var localeValue) && !string.IsNullOrEmpty(localeValue))
            {
                result[key] = localeValue;
            }
            else if (englishOverlay.TryGetValue(key, out var englishValue) && !string.IsNullOrEmpty(englishValue))
            {
                result[key] = englishValue;
            }
            else
            {
                result[key] = key; // Raw key as fallback
            }
        }

        if (localeOverlay != null)
        {
            foreach (var key in localeOverlay.Keys)
            {
                if (!result.ContainsKey(key))
                {
                    result[key] = localeOverlay[key];
                }
            }
        }

        return result;
    }

    private static async Task<IResult> LoadGameData(
        IGamePathService pathService,
        IGameDataService dataService,
        CancellationToken cancellationToken)
    {
        if (!pathService.IsPathSet)
        {
            return Results.BadRequest(new ErrorDto(
                "No game path configured",
                "Please set the game path first using POST /api/game/path or GET /api/game/detect"
            ));
        }

        // Attempt to load
        var success = await dataService.LoadAsync(cancellationToken);

        var dto = new GameStatusDto(
            PathSet: pathService.IsPathSet,
            DataLoaded: dataService.IsLoaded,
            GameRoot: pathService.GameRoot,
            HeroesOeDataPath: pathService.HeroesOeDataPath,
            StreamingAssetsPath: pathService.StreamingAssetsPath,
            CurrentLocale: pathService.CurrentLocale,
            Error: dataService.Error,
            UiLabels: BuildUiLabels(pathService.CurrentLocale)
        );

        if (!success)
        {
            return Results.Json(
                new ErrorDto("Failed to load game data", dataService.Error),
                statusCode: 503
            );
        }

        return Results.Ok(dto);
    }

    private static IResult ClearGamePath(IGamePathService pathService)
    {
        pathService.Clear();
        return Results.Ok(new MessageDto("Game path cleared"));
    }
}
