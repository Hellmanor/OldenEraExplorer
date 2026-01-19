using API.Contracts;
using API.Services;

namespace API.Endpoints;

public static class ViewerEndpoints
{
    public static IEndpointRouteBuilder MapViewerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/viewer")
            .WithTags("Viewer");

        group.MapGet("/platform", GetPlatformGlb)
            .WithName("GetPlatformGlb")
            .WithSummary("Get the platform GLB file for Game Preview mode")
            .Produces(200, contentType: "model/gltf-binary")
            .Produces<ErrorDto>(404);

        group.MapGet("/background", GetBackground)
            .WithName("GetViewerBackground")
            .WithSummary("Get the background texture for Game Preview mode")
            .Produces(200, contentType: "image/png")
            .Produces<ErrorDto>(404);

        group.MapGet("/environment", GetEnvironment)
            .WithName("GetViewerEnvironment")
            .WithSummary("Get the equirectangular environment map for IBL lighting")
            .Produces(200, contentType: "image/png")
            .Produces<ErrorDto>(404);

        return endpoints;
    }

    private static IResult GetPlatformGlb(IAssetServingService assetService)
    {
        var glbPath = ResolvePlatformGlbPath(assetService.ExtractedAssetsDirectory);

        if (glbPath == null || !File.Exists(glbPath))
        {
            return Results.NotFound(new ErrorDto("Platform model not found"));
        }

        var fileBytes = File.ReadAllBytes(glbPath);
        return Results.File(fileBytes, "model/gltf-binary", "platform.glb");
    }

    private static IResult GetBackground(IAssetServingService assetService)
    {
        var path = ResolveAssetPath(assetService.ExtractedAssetsDirectory, "Assets", "Texture2D", "unit_info_back.png");

        if (path == null || !File.Exists(path))
        {
            return Results.NotFound(new ErrorDto("Background texture not found. Run extract-textures in unity-asset-to-glb."));
        }

        var fileBytes = File.ReadAllBytes(path);
        return Results.File(fileBytes, "image/png", "unit_info_back.png");
    }

    private static IResult GetEnvironment(IAssetServingService assetService)
    {
        var path = ResolveAssetPath(assetService.ExtractedAssetsDirectory, "Assets", "Cubemap", "Cold Sunset Equirect.png");

        if (path == null || !File.Exists(path))
        {
            return Results.NotFound(new ErrorDto("Environment map not found. Run extract-textures in unity-asset-to-glb."));
        }

        var fileBytes = File.ReadAllBytes(path);
        return Results.File(fileBytes, "image/png", "Cold Sunset Equirect.png");
    }

    private static string? ResolvePlatformGlbPath(string extractedDir)
    {
        return ResolveAssetPath(extractedDir, "Assets", "GameObject", "PLATFORM + BACK.glb");
    }

    private static string? ResolveAssetPath(string extractedDir, params string[] pathParts)
    {
        if (!Directory.Exists(extractedDir))
            return null;

        // Search in all version directories (newest first based on directory name)
        var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*")
            .OrderByDescending(d => d);

        foreach (var versionDir in versionDirs)
        {
            var assetPath = Path.Combine(new[] { versionDir }.Concat(pathParts).ToArray());
            if (File.Exists(assetPath))
                return assetPath;
        }

        return null;
    }
}
