using API.Contracts;
using API.Services;

namespace API.Endpoints;

/// <summary>
/// API endpoints for serving extracted game assets.
/// </summary>
public static class AssetsEndpoints
{
    /// <summary>
    /// Maps all asset-serving endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapAssetsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/assets")
            .WithTags("Assets")
            ;

        // GET /api/assets/png/{*path} - Serve PNG icon
        group.MapGet("/png/{*path}", ServePngIcon)
            .WithName("ServePngIcon")
            .WithSummary("Serve PNG icon")
            .WithDescription("Returns the PNG file for the specified icon path. Returns 404 if not found.")
            .Produces(200, contentType: "image/png")
            .Produces<ErrorDto>(404);

        // GET /api/assets/icons - List all extracted icons
        group.MapGet("/icons", ListExtractedIcons)
            .WithName("ListExtractedIcons")
            .WithSummary("List all available icons")
            .WithDescription("Returns a list of all available icon paths.")
            .Produces<List<string>>(200);

        // GET /api/assets/exists/png/{*path} - Check if icon exists
        group.MapGet("/exists/png/{*path}", CheckIconExists)
            .WithName("CheckIconExists")
            .WithSummary("Check if icon exists")
            .WithDescription("Returns whether the specified icon exists.")
            .Produces<IconExistsDto>(200);

        // GET /api/assets/info - Get asset serving information
        group.MapGet("/info", GetAssetsInfo)
            .WithName("GetAssetsInfo")
            .WithSummary("Get asset serving information")
            .WithDescription("Returns information about the asset serving configuration.")
            .Produces<AssetsInfoDto>(200);

        return endpoints;
    }

    /// <summary>
    /// Serves a PNG icon file.
    /// </summary>
    private static async Task<IResult> ServePngIcon(
        string path,
        string? version,
        IAssetServingService assetService)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Results.BadRequest(new ErrorDto("Path is required"));
        }

        var resolvedPath = assetService.ResolveIconPath(path, version);

        if (resolvedPath != null && File.Exists(resolvedPath))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(resolvedPath);
                return Results.Bytes(bytes, contentType: "image/png", fileDownloadName: Path.GetFileName(resolvedPath));
            }
            catch (IOException)
            {
                return Results.StatusCode(503);
            }
        }

        var iconFileName = Path.GetFileName(path);
        if (!iconFileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            iconFileName += ".png";
        }

        var resourceName = $"CustomAssets/{iconFileName}";
        var assembly = typeof(Program).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream != null)
        {
            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes);
            return Results.Bytes(bytes, contentType: "image/png", fileDownloadName: iconFileName);
        }

        return Results.NotFound(new ErrorDto(
            "Icon not found",
            $"The icon '{path}' was not found."
        ));
    }

    /// <summary>
    /// Lists all extracted icons.
    /// </summary>
    private static IResult ListExtractedIcons(
        IAssetServingService assetService,
        string? filter = null)
    {
        var allIcons = assetService.GetExtractedIcons();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            allIcons = allIcons.Where(i =>
                i.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Results.Ok(allIcons);
    }

    /// <summary>
    /// Checks if an icon exists.
    /// </summary>
    private static IResult CheckIconExists(
        string path,
        IAssetServingService assetService)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Results.BadRequest(new ErrorDto("Path is required"));
        }

        var exists = assetService.IconExists(path);
        var resolvedPath = exists ? assetService.ResolveIconPath(path) : null;

        return Results.Ok(new IconExistsDto(
            Path: path,
            Exists: exists,
            ResolvedPath: resolvedPath
        ));
    }

    /// <summary>
    /// Gets information about the asset serving configuration.
    /// </summary>
    private static IResult GetAssetsInfo(IAssetServingService assetService)
    {
        var extractedIcons = assetService.GetExtractedIcons();

        return Results.Ok(new AssetsInfoDto(
            ExtractedAssetsDirectory: assetService.ExtractedAssetsDirectory,
            CurrentVersion: assetService.CurrentVersion,
            TotalExtractedIcons: extractedIcons.Count,
            DirectoryExists: Directory.Exists(assetService.ExtractedAssetsDirectory)
        ));
    }

}

// Asset DTOs =====

/// <summary>
/// Result of checking if an icon exists.
/// </summary>
/// <param name="Path">The requested icon path.</param>
/// <param name="Exists">Whether the icon exists.</param>
/// <param name="ResolvedPath">The resolved file system path, if it exists.</param>
public record IconExistsDto(
    string Path,
    bool Exists,
    string? ResolvedPath
);

/// <summary>
/// Information about the asset serving configuration.
/// </summary>
/// <param name="ExtractedAssetsDirectory">Base directory where assets are stored.</param>
/// <param name="CurrentVersion">Current game version for asset resolution.</param>
/// <param name="TotalExtractedIcons">Total number of available icons.</param>
/// <param name="DirectoryExists">Whether the assets directory exists.</param>
public record AssetsInfoDto(
    string ExtractedAssetsDirectory,
    string? CurrentVersion,
    int TotalExtractedIcons,
    bool DirectoryExists
);
