using API.Endpoints;

namespace API.Extensions;

public static class EndpointExtensions
{
    public static WebApplication MapAllEndpoints(this WebApplication app)
    {
        app.MapGameEndpoints();
        app.MapUnitsEndpoints();
        app.MapSubclassesEndpoints();
        app.MapAbilitiesEndpoints();
        app.MapBuildingsEndpoints();
        app.MapArtifactsEndpoints();
        app.MapHeroesEndpoints();
        app.MapFactionLawsEndpoints();
        app.MapMapObjectsEndpoints();
        app.MapSkillsEndpoints();
        app.MapSpellsEndpoints();
        app.MapAssetsEndpoints();
        app.MapSearchEndpoints();
        app.MapSettingsEndpoints();
        app.MapFilesystemEndpoints();
        app.MapLabelsEndpoints();
        app.MapReferencesEndpoints();
        app.MapModelsEndpoints();
        app.MapViewerEndpoints();
        app.MapExtractionEndpoints();

        return app;
    }
}
