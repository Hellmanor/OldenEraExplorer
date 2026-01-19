using Localization.Services;
using API.Services;

namespace API.Endpoints;

public static class LabelsEndpoints
{
    public static IEndpointRouteBuilder MapLabelsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/labels")
            .WithTags("Labels");

        // GET /api/labels - Get all UI labels for the current locale
        group.MapGet("/", GetLabels)
            .WithName("GetLabels")
            .WithSummary("Get UI labels")
            .WithDescription("Returns localized UI labels for column headers and other UI elements.")
            .Produces<LabelsDto>(200);

        return endpoints;
    }

    private static IResult GetLabels(IGamePathService pathService, IGameDataService dataService)
    {
        var locale = pathService.CurrentLocale ?? "english";
        var overlay = OverlayService.Instance;
        var lang = dataService.Data?.Lang;

        // Helper: resolve from overlay first, then LangIndex, then fallback
        string Resolve(string? overlaySid, string? langSid, string fallback)
        {
            // 1. Try overlay (if overlaySid provided)
            if (overlaySid is not null)
            {
                var result = overlay.TryResolveFromOverlay(overlaySid, locale);
                if (!string.IsNullOrEmpty(result)) return result;
            }

            // 2. Try LangIndex (game JSON)
            if (lang is not null && langSid is not null)
            {
                var result = lang.ResolveText(langSid);
                if (!string.IsNullOrEmpty(result)) return result;
            }

            // 3. Fallback
            return fallback;
        }

        var overlayDict = overlay.GetOverlayForLocale(locale);
        var overlayTexts = overlayDict != null
            ? new Dictionary<string, string>(overlayDict)
            : new Dictionary<string, string>();

        var dto = new LabelsDto(
            Columns: new ColumnLabelsDto(
                Name: Resolve(null, "tab_name", "Name"),           // Only from LangIndex
                Tier: Resolve("label_unit_tier", null, "Tier"),       // Only from overlay
                Faction: Resolve(null, "fractions", "Faction"),    // Only from LangIndex
                Id: Resolve("label_id", null, "ID"),           // Only from overlay
                Category: Resolve("label_category", null, "Category"),
                School: Resolve("label_spell_school", null, "Magic School"),
                Level: Resolve("label_level", null, "Level"),  // Overlay (no game key exists)
                Class: Resolve("label_hero_class", null, "Class")     // From Overlay
            ),
            Overlay: overlayTexts
        );

        return Results.Ok(dto);
    }
}

// Labels DTOs =====

/// <param name="Columns">Column header labels for data grids and sorting buttons.</param>
/// <param name="Overlay">All overlay texts for UI elements (navigation, buttons, messages, etc.).</param>
public record LabelsDto(
    ColumnLabelsDto Columns,
    Dictionary<string, string> Overlay
);

/// <param name="Name">Label for "Name" column.</param>
/// <param name="Tier">Label for "Tier" column (units, spells).</param>
/// <param name="Faction">Label for "Faction" column.</param>
/// <param name="Id">Label for "ID" column.</param>
/// <param name="Category">Label for "Category" column (spells).</param>
/// <param name="School">Label for "School" column (spells - magic school).</param>
/// <param name="Level">Label for "Level" column (buildings).</param>
/// <param name="Class">Label for "Class" column (heroes).</param>
public record ColumnLabelsDto(
    string Name,
    string Tier,
    string Faction,
    string Id,
    string Category,
    string School,
    string Level,
    string Class
);
