using GameData.Indexing;
using GameData.Services;
using Localization.Indexing;
using Localization.Resolution;
using API.Contracts;
using API.Services;

namespace API.Endpoints;

/// <summary>
/// Model catalog endpoints for listing and serving 3D models (units and map objects).
/// GLB files are served from the ExtractedAssets directory.
/// </summary>
public static class ModelsEndpoints
{
    // Orphan GLB suffix - used when orphan GLB filename collides with existing unit ID
    private const string OrphanGlbSuffix = "_glb";

    /// <summary>
    /// Map all model-related endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapModelsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/models")
            .WithTags("Models")
            ;

        group.MapGet("/units", ListUnitModels)
            .WithName("ListUnitModels")
            .WithSummary("List all available unit models")
            .Produces<List<UnitListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/map-objects", ListMapObjectModels)
            .WithName("ListMapObjectModels")
            .WithSummary("List all available map object models")
            .Produces<List<MapObjectListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/artifacts", ListArtifactModels)
            .WithName("ListArtifactModels")
            .WithSummary("List all available artifact models")
            .Produces<List<ArtifactListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/extracted", ListExtractedModels)
            .WithName("ListExtractedModels")
            .WithSummary("List all models that have been extracted")
            .Produces<List<ExtractedModelDto>>(200);

        // GLB serving endpoints
        group.MapGet("/unit/{id}/glb", GetUnitModelGlb)
            .WithName("GetUnitModelGlb")
            .WithSummary("Get a unit's GLB file")
            .Produces(200, contentType: "model/gltf-binary")
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        // Map object endpoints use category/name as separate path segments
        group.MapGet("/map-object/{category}/{name}/glb", GetMapObjectModelGlb)
            .WithName("GetMapObjectModelGlb")
            .WithSummary("Get a map object's GLB file")
            .Produces(200, contentType: "model/gltf-binary")
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        // Status endpoints
        group.MapGet("/unit/{id}/status", GetUnitModelStatus)
            .WithName("GetUnitModelStatus")
            .WithSummary("Check if a unit's GLB file exists")
            .Produces<ModelStatusDto>(200);

        group.MapGet("/map-object/{category}/{name}/status", GetMapObjectModelStatus)
            .WithName("GetMapObjectModelStatus")
            .WithSummary("Check if a map object's GLB file exists")
            .Produces<ModelStatusDto>(200);

        return endpoints;
    }

    /// <summary>
    /// List all available unit models (from DB + orphan GLBs).
    /// </summary>
    private static IResult ListUnitModels(
        IGameDataService dataService,
        IAssetServingService assetService,
        IGamePathService gamePathService,
        FactionMapper factionMapper,
        string? search = null)
    {
        var items = new List<UnitListItemDto>();

        if (dataService.IsLoaded && dataService.Data is not null)
        {
            var data = dataService.Data;
            var lang = data.Lang;
            var resolver = data.ResolverFacade;
            var locale = gamePathService.CurrentLocale;
            IEnumerable<GameData.Indexing.DbIndex.UnitRecord> units = data.Units;

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchTerm = search.Trim();
                var tierLabel = lang.ResolveText("label_unit_tier");

                units = units.Where(u =>
                {
                    if (u.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (u.Fraction.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var factionDisplay = factionMapper.MapFactionDisplay(u.Fraction);
                    if (factionDisplay?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                        return true;

                    if (u.Tier.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var tierText = !string.IsNullOrWhiteSpace(tierLabel) && tierLabel != "label_unit_tier"
                        ? $"{tierLabel} {u.Tier}"
                        : $"Tier {u.Tier}";
                    if (tierText.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var localizedName = GetLocalizedUnitName(resolver, lang, u.Id, locale);
                    if (localizedName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                        return true;

                    return false;
                });
            }

            // Mesh-based deduplication (Viewer is a GLB browser)
            var dedupedUnits = DeduplicateByMesh(
                units,
                u => u.Mesh,
                u => u.Id);

            items.AddRange(dedupedUnits.Select(u => new UnitListItemDto(
                u.Id,
                GetLocalizedUnitName(resolver, lang, u.Id, locale) ?? u.Id,
                string.IsNullOrEmpty(u.Fraction) ? null : u.Fraction,
                factionMapper.MapFactionDisplay(u.Fraction),
                u.Tier > 0 ? u.Tier : null,
                $"icons/units/hex_portraits/{u.Id}",
                IsOrphan: false,
                Scale: u.Scale,
                PrefabPath: u.Mesh)));
        }

        var extractedDir = assetService.ExtractedAssetsDirectory;
        var langForOrphans = dataService.IsLoaded && dataService.Data is not null
            ? dataService.Data.Lang
            : null;

        if (Directory.Exists(extractedDir))
        {
            var knownModelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingUnitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (dataService.IsLoaded && dataService.Data is not null)
            {
                foreach (var unit in dataService.Data.Units)
                {
                    existingUnitIds.Add(unit.Id);

                    if (!string.IsNullOrEmpty(unit.Mesh))
                    {
                        var meshModelName = Path.GetFileName(unit.Mesh);
                        knownModelNames.Add(meshModelName);
                    }
                }
            }

            // Pre-build icon suffix lookup for performance
            var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*");
            var oldIconVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var versionDir in versionDirs)
            {
                var iconsDir = Path.Combine(versionDir, "Assets", "Resources", "icons", "units", "hex_portraits");
                if (Directory.Exists(iconsDir))
                {
                    foreach (var oldIcon in Directory.GetFiles(iconsDir, "*_old.png"))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(oldIcon);
                        if (fileName.EndsWith("_old", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseName = fileName.Substring(0, fileName.Length - "_old".Length);
                            oldIconVariants.Add(baseName);
                        }
                    }
                }
            }

            var orphanGlbs = new Dictionary<string, (string path, string faction, string glbFileName)>(StringComparer.OrdinalIgnoreCase);

            foreach (var versionDir in versionDirs)
            {
                var unitsDir = Path.Combine(versionDir, "Assets", "Resources", "units");
                if (!Directory.Exists(unitsDir))
                    continue;

                foreach (var glbFile in Directory.GetFiles(unitsDir, "*.glb", SearchOption.AllDirectories))
                {
                    var glbFileName = Path.GetFileNameWithoutExtension(glbFile);

                    if (knownModelNames.Contains(glbFileName))
                        continue;

                    // Generate unique orphan ID (avoid collisions with unit IDs)
                    var orphanId = glbFileName;
                    var suffixCounter = 1;
                    while (existingUnitIds.Contains(orphanId))
                    {
                        orphanId = suffixCounter == 1
                            ? glbFileName + OrphanGlbSuffix
                            : $"{glbFileName}{OrphanGlbSuffix}{suffixCounter}";
                        suffixCounter++;
                    }

                    if (orphanGlbs.ContainsKey(orphanId))
                        continue;

                    var relativePath = Path.GetRelativePath(unitsDir, glbFile);
                    var parts = relativePath.Split(Path.DirectorySeparatorChar);
                    var faction = parts.Length > 1 ? parts[0] : null;

                    orphanGlbs[orphanId] = (glbFile, faction ?? "unknown", glbFileName);
                }
            }

            foreach (var (orphanId, (path, faction, glbFileName)) in orphanGlbs)
            {
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchTerm = search.Trim();

                    // Special case: "orphan"/"unused" search shows all orphan GLBs regardless of name
                    var isOrphanSearch = searchTerm.Equals("orphan", StringComparison.OrdinalIgnoreCase) ||
                                         searchTerm.Equals("unused", StringComparison.OrdinalIgnoreCase);

                    if (!isOrphanSearch)
                    {
                        var factionDisplay = factionMapper.MapFactionDisplay(faction);

                        if (!glbFileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                            !faction.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                            (factionDisplay == null || !factionDisplay.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)))
                            continue;
                    }
                }

                string iconSuffix = oldIconVariants.Contains(glbFileName) ? "_old" : "_upg";

                items.Add(new UnitListItemDto(
                    orphanId,
                    glbFileName,
                    faction,
                    factionMapper.MapFactionDisplay(faction),
                    null,
                    $"icons/units/hex_portraits/{glbFileName}{iconSuffix}",
                    IsOrphan: true,
                    Scale: null,
                    PrefabPath: null
                ));
            }
        }

        // DB units first, then orphans, both alphabetically by faction and name
        items = items
            .OrderBy(x => x.IsOrphan)
            .ThenBy(x => x.Faction)
            .ThenBy(x => x.Name)
            .ToList();

        return Results.Ok(items);
    }

    /// <summary>
    /// List all available map object models for the 3D viewer.
    /// Returns unique GLB meshes only - multiple JSON items sharing the same mesh show once.
    /// Includes orphan GLBs (exist on disk but not in DB).
    /// </summary>
    private static IResult ListMapObjectModels(
        IGameDataService dataService,
        IAssetServingService assetService,
        IGamePathService gamePathService,
        FactionMapper factionMapper,
        string? search = null,
        string? category = null)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(new ErrorDto("Game data not loaded"), statusCode: 503);
        }

        var data = dataService.Data;
        var lang = data.Lang;
        var resolver = data.ResolverFacade;
        var locale = gamePathService.CurrentLocale;

        // EXCLUDE artifacts - they have separate endpoint
        IEnumerable<MapObjectsIndex.MapObjectRecord> mapObjects =
            data.MapObjectsIndex.MapObjects.Values
                .Where(mo => mo.IsInteractable)
                .Where(mo => !string.Equals(mo.Tag, "Artifact", StringComparison.OrdinalIgnoreCase));

        // 2. Build expected GLB names BEFORE search filter (for orphan detection)
        //    Use FULL list to avoid false orphans when search returns no results
        var expectedGlbNames = data.MapObjectsIndex.MapObjects.Values
            .Where(mo => mo.IsInteractable)
            .Where(mo => !string.Equals(mo.Tag, "Artifact", StringComparison.OrdinalIgnoreCase))
            .Select(mo => mo.PrefabPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(path => Path.GetFileName(path)) // NO .glb extension
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(category))
        {
            mapObjects = mapObjects.Where(mo =>
                category.Equals(GetCategory(mo.PrefabPath), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            mapObjects = mapObjects.Where(mo =>
                mo.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                GetMapObjectLocalizedName(mo, lang, resolver, locale)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true);
        }

        // Mesh-based deduplication (viewer GLB browser)
        var dedupedMapObjects = DeduplicateByMesh(
            mapObjects,
            mo => mo.PrefabPath,
            mo => mo.Id);

        var items = dedupedMapObjects
            .Select(mo => MapToMapObjectModelItem(mo, lang, resolver, locale))
            .ToList();

        var extractedDir = assetService.ExtractedAssetsDirectory;
        if (Directory.Exists(extractedDir))
        {
            var categories = new[] { "barracks", "interactive", "resource" };
            var actualGlbFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var glbPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // name -> category/name

            var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*");
            foreach (var versionDir in versionDirs)
            {
                foreach (var cat in categories)
                {
                    var glbDir = Path.Combine(versionDir, "Assets", "Resources", "objects", cat);
                    if (!Directory.Exists(glbDir)) continue;

                    var files = Directory.GetFiles(glbDir, "*.glb", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var relativePath = Path.GetRelativePath(glbDir, file);

                        if (relativePath.Contains("debug_objects", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (name.Equals("magic_portal_hex", StringComparison.OrdinalIgnoreCase))
                            continue;

                        actualGlbFiles.Add(name);
                        // Store full path as category/name (e.g., "interactive/castle")
                        glbPathMap[name] = $"{cat}/{name}";
                    }
                }
            }

            // Orphans = Actual GLBs MINUS Expected GLBs (from JSON PrefabPaths)
            var orphanGlbs = actualGlbFiles.Except(expectedGlbNames, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var orphanName in orphanGlbs)
            {
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchTerm = search.Trim();

                    // Special case: "orphan"/"unused" search shows all orphan GLBs regardless of name
                    var isOrphanSearch = searchTerm.Equals("orphan", StringComparison.OrdinalIgnoreCase) ||
                                         searchTerm.Equals("unused", StringComparison.OrdinalIgnoreCase);

                    if (!isOrphanSearch && !orphanName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var prefabPath = glbPathMap.TryGetValue(orphanName, out var path) ? path : null;

                var orphanCategory = prefabPath?.Split('/')[0] ?? "Unknown";

                if (!string.IsNullOrWhiteSpace(category))
                {
                    if (!orphanCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                items.Add(new MapObjectListItemDto(
                    orphanName,
                    orphanName,
                    orphanCategory,
                    "icons/placeholder",
                    IsOrphan: true,
                    PrefabPath: prefabPath));
            }
        }

        return Results.Ok(items);
    }

    /// <summary>
    /// List all available artifact models for the 3D viewer.
    /// Returns unique PrefabPath-based artifacts - magic scrolls are deduplicated by mesh.
    /// </summary>
    private static IResult ListArtifactModels(
        IGameDataService dataService,
        IAssetServingService assetService,
        IGamePathService gamePathService,
        FactionMapper factionMapper,
        string? search = null)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(new ErrorDto("Game data not loaded"), statusCode: 503);
        }

        var data = dataService.Data;
        var lang = data.Lang;
        var artifactsIndex = data.ArtifactsIndex;
        var mapObjectsIndex = data.MapObjectsIndex;
        var items = new List<ArtifactListItemDto>();

        // JOIN: ArtifactsIndex (metadata) + MapObjectsIndex (PrefabPath)
        var artifactsWithPath = artifactsIndex.Artifacts.Values
            .Where(a => HasArtifactLocalization(a, lang))
            .Select(a =>
            {
                MapObjectsIndex.MapObjectRecord? mapObject = null;

                if (mapObjectsIndex.MapObjects.TryGetValue(a.Id, out var mo))
                {
                    mapObject = mo;
                }
                // Special case: scroll boxes (artifact ID contains scroll, but MapObject ID is different)
                else if (a.Id.Contains("scroll_artifact", StringComparison.OrdinalIgnoreCase))
                {
                    string? scrollBoxId = null;
                    if (a.Id.StartsWith("mythic_magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
                        scrollBoxId = "mythic_scroll_box";
                    else if (a.Id.StartsWith("enchanted_magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
                        scrollBoxId = "enchanted_scroll_box";
                    else if (a.Id.StartsWith("magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
                        scrollBoxId = "scroll_box";

                    if (scrollBoxId != null && mapObjectsIndex.MapObjects.TryGetValue(scrollBoxId, out mo))
                    {
                        mapObject = mo;
                    }
                }

                return new
                {
                    Artifact = a,
                    MapObject = mapObject
                };
            })
            .Where(x => x.MapObject != null)
            .ToList();

        // Use FULL list to avoid false orphans when search returns no results
        var existingPrefabPaths = new HashSet<string>(
            artifactsWithPath
                .Select(x => x.MapObject?.PrefabPath)
                .Where(p => !string.IsNullOrEmpty(p))!,
            StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            artifactsWithPath = artifactsWithPath.Where(x =>
                x.Artifact.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (lang.ResolveText(x.Artifact.NameSid)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true) ||
                x.Artifact.Rarity.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Artifact.Slot.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (GetArtifactRaritySlotText(lang, x.Artifact.Rarity, x.Artifact.Slot)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true))
                .ToList();
        }

        // Viewer is a GLB browser - deduplicate by PrefabPath to show unique 3D models
        var dedupedArtifacts = DeduplicateByMesh(
            artifactsWithPath,
            item => item.MapObject?.PrefabPath,
            item => item.Artifact.Id);

        foreach (var item in dedupedArtifacts)
        {
            var localizedName = lang.ResolveText(item.Artifact.NameSid);
            var raritySlotText = GetArtifactRaritySlotText(lang, item.Artifact.Rarity, item.Artifact.Slot);
            var prefabPath = item.MapObject?.PrefabPath ?? $"artifact/{item.Artifact.Id}";

            // Clean ID: strip "artifact/" prefix for cleaner URLs
            var cleanId = prefabPath.StartsWith("artifact/", StringComparison.OrdinalIgnoreCase)
                ? prefabPath.Substring("artifact/".Length)
                : prefabPath;

            items.Add(new ArtifactListItemDto(
                cleanId,
                localizedName ?? item.Artifact.Id,
                item.Artifact.Rarity,
                item.Artifact.Slot,
                raritySlotText,
                $"icons/artifacts/{item.Artifact.Icon}",
                IsOrphan: false,
                PrefabPath: prefabPath)); // Full path for GLB serving
        }

        var extractedDir = assetService.ExtractedAssetsDirectory;
        if (Directory.Exists(extractedDir))
        {
            var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*");
            var orphanGlbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var versionDir in versionDirs)
            {
                var artifactsDir = Path.Combine(versionDir, "Assets", "Resources", "objects", "artifact");
                if (!Directory.Exists(artifactsDir))
                    continue;

                foreach (var glbFile in Directory.GetFiles(artifactsDir, "*.glb", SearchOption.AllDirectories))
                {
                    var glbFileName = Path.GetFileNameWithoutExtension(glbFile);
                    var prefabPath = $"artifact/{glbFileName}";

                    if (existingPrefabPaths.Contains(prefabPath) || orphanGlbs.ContainsKey(prefabPath))
                        continue;

                    orphanGlbs[prefabPath] = glbFile;
                }
            }

            foreach (var (prefabPath, path) in orphanGlbs)
            {
                var glbFileName = Path.GetFileName(prefabPath);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchTerm = search.Trim();

                    // Special case: "orphan"/"unused" search shows all orphan GLBs regardless of name
                    var isOrphanSearch = searchTerm.Equals("orphan", StringComparison.OrdinalIgnoreCase) ||
                                         searchTerm.Equals("unused", StringComparison.OrdinalIgnoreCase);

                    if (!isOrphanSearch && !glbFileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Clean ID: strip "artifact/" prefix for cleaner URLs
                var cleanId = prefabPath.StartsWith("artifact/", StringComparison.OrdinalIgnoreCase)
                    ? prefabPath.Substring("artifact/".Length)
                    : glbFileName;

                items.Add(new ArtifactListItemDto(
                    cleanId,
                    glbFileName,
                    null,
                    null,
                    null,
                    $"icons/artifacts/{glbFileName}",
                    IsOrphan: true,
                    PrefabPath: prefabPath)); // Full path for GLB serving
            }
        }

        var result = items
            .OrderBy(x => x.IsOrphan)
            .ThenBy(x => x.Name)
            .ToList();

        return Results.Ok(result);
    }

    /// <summary>
    /// Maps a MapObjectRecord to a MapObjectListItemDto with localized data.
    /// </summary>
    private static MapObjectListItemDto MapToMapObjectModelItem(
        MapObjectsIndex.MapObjectRecord mapObject,
        LangIndex lang,
        ITextResolver resolver,
        string? locale)
    {
        var prefabPath = mapObject.PrefabPath;
        var prefabCategory = GetCategory(prefabPath);
        var prefabName = GetName(prefabPath);

        var name = GetMapObjectLocalizedName(mapObject, lang, resolver, locale) ?? prefabName;
        var icon = BuildMapObjectIconPath(prefabPath);

        // Clean ID: use just the name without category prefix for cleaner URLs
        return new MapObjectListItemDto(
            prefabName,
            name,
            prefabCategory,
            icon,
            IsOrphan: false,
            PrefabPath: prefabPath); // Full path for GLB serving
    }

    /// <summary>
    /// Gets the localized name for a map object.
    /// Uses the same fallback logic as MapObjectsEndpoints.GetLocalizedName.
    /// </summary>
    private static string? GetMapObjectLocalizedName(
        MapObjectsIndex.MapObjectRecord mapObject,
        LangIndex lang,
        ITextResolver resolver,
        string? locale)
    {
        if (!string.IsNullOrWhiteSpace(mapObject.NameSid))
        {
            var result = TryResolveText(resolver, mapObject.NameSid, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(mapObject.NameSid);
            // Check that the result is not the same as the SID (means lookup failed)
            if (!string.IsNullOrWhiteSpace(langResult) && langResult != mapObject.NameSid)
                return langResult;
        }

        // Fallback to pattern-based SIDs (same logic as MapObjectsEndpoints)
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
            if (!string.IsNullOrWhiteSpace(langResult) && langResult != pattern)
                return langResult;
        }

        return null;
    }

    /// <summary>
    /// Builds the icon path from a prefab path for map objects.
    /// </summary>
    private static string? BuildMapObjectIconPath(string? prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
            return null;

        // Map objects use Resources/objects/ folder for icons
        return $"objects/{prefabPath.Replace('\\', '/')}";
    }

    /// <summary>
    /// List all extracted GLB models from the ExtractedAssets directory.
    /// </summary>
    private static IResult ListExtractedModels(IAssetServingService assetService)
    {
        var extractedDir = assetService.ExtractedAssetsDirectory;
        var extractedModels = new List<ExtractedModelDto>();

        if (!Directory.Exists(extractedDir))
        {
            return Results.Ok(extractedModels);
        }

        var glbFiles = Directory.GetFiles(extractedDir, "*.glb", SearchOption.AllDirectories);

        foreach (var file in glbFiles)
        {
            var fileInfo = new FileInfo(file);
            var relativePath = Path.GetRelativePath(extractedDir, file).Replace('\\', '/');

            var isUnit = relativePath.Contains("/units/", StringComparison.OrdinalIgnoreCase);
            var isMapObject = relativePath.Contains("/objects/", StringComparison.OrdinalIgnoreCase);

            string id;
            string type;

            if (isUnit)
            {
                id = Path.GetFileNameWithoutExtension(file);
                type = "unit";
            }
            else if (isMapObject)
            {
                var parts = relativePath.Split('/');
                var objectsIndex = Array.FindIndex(parts, p => p.Equals("objects", StringComparison.OrdinalIgnoreCase));
                if (objectsIndex >= 0 && objectsIndex + 2 < parts.Length)
                {
                    var cat = parts[objectsIndex + 1];
                    var name = Path.GetFileNameWithoutExtension(parts[objectsIndex + 2]);
                    id = $"{cat}/{name}";
                }
                else
                {
                    id = Path.GetFileNameWithoutExtension(file);
                }
                type = "map-object";
            }
            else
            {
                continue; // Skip unknown GLB files
            }

            extractedModels.Add(new ExtractedModelDto(
                id,
                type,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc));
        }

        var result = extractedModels
            .GroupBy(m => (m.Id, m.Type))
            .Select(g => g.OrderByDescending(m => m.LastExtractedUtc).First())
            .OrderByDescending(m => m.LastExtractedUtc)
            .ToList();

        return Results.Ok(result);
    }

    /// <summary>
    /// Get a unit's GLB file.
    /// </summary>
    private static IResult GetUnitModelGlb(
        string id,
        IAssetServingService assetService,
        IGameDataService dataService)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(new ErrorDto("Game data not loaded"), statusCode: 503);
        }

        var unit = dataService.Data.Units.FirstOrDefault(u =>
            u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        var glbPath = ResolveUnitGlbPath(
            assetService.ExtractedAssetsDirectory,
            assetService.CurrentVersion,
            id,
            unit?.Fraction,
            unit?.Mesh);

        if (glbPath == null || !File.Exists(glbPath))
        {
            return Results.NotFound(new ErrorDto($"Model '{id}' not found"));
        }

        var fileBytes = File.ReadAllBytes(glbPath);
        return Results.File(fileBytes, "model/gltf-binary", $"{id}.glb");
    }

    /// <summary>
    /// Get a map object's GLB file.
    /// </summary>
    private static IResult GetMapObjectModelGlb(
        string category,
        string name,
        IAssetServingService assetService)
    {
        var glbPath = ResolveMapObjectGlbPath(
            assetService.ExtractedAssetsDirectory,
            assetService.CurrentVersion,
            category,
            name);

        if (glbPath == null || !File.Exists(glbPath))
        {
            return Results.NotFound(new ErrorDto($"Model '{category}/{name}' not found"));
        }

        var fileBytes = File.ReadAllBytes(glbPath);
        var filename = $"{category}_{name}.glb";
        return Results.File(fileBytes, "model/gltf-binary", filename);
    }

    /// <summary>
    /// Check if a unit's GLB file exists.
    /// </summary>
    private static IResult GetUnitModelStatus(
        string id,
        IAssetServingService assetService,
        IGameDataService dataService)
    {
        string? faction = null;
        string? mesh = null;
        if (dataService.IsLoaded && dataService.Data is not null)
        {
            var unit = dataService.Data.Units.FirstOrDefault(u =>
                u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            faction = unit?.Fraction;
            mesh = unit?.Mesh;
        }

        var glbPath = ResolveUnitGlbPath(
            assetService.ExtractedAssetsDirectory,
            assetService.CurrentVersion,
            id,
            faction,
            mesh);

        if (glbPath != null && File.Exists(glbPath))
        {
            var fileInfo = new FileInfo(glbPath);
            return Results.Ok(new ModelStatusDto(true, fileInfo.Length, fileInfo.LastWriteTimeUtc));
        }

        return Results.Ok(new ModelStatusDto(false, null, null));
    }

    /// <summary>
    /// Check if a map object's GLB file exists.
    /// </summary>
    private static IResult GetMapObjectModelStatus(
        string category,
        string name,
        IAssetServingService assetService)
    {
        var glbPath = ResolveMapObjectGlbPath(
            assetService.ExtractedAssetsDirectory,
            assetService.CurrentVersion,
            category,
            name);

        if (glbPath != null && File.Exists(glbPath))
        {
            var fileInfo = new FileInfo(glbPath);
            return Results.Ok(new ModelStatusDto(true, fileInfo.Length, fileInfo.LastWriteTimeUtc));
        }

        return Results.Ok(new ModelStatusDto(false, null, null));
    }

    /// <summary>
    /// Resolves a unit GLB path by searching through ExtractedAssets directories.
    /// Path pattern: Assets-{version}/Assets/Resources/units/{faction}/{model_name}.glb
    /// Uses mesh (prefab path) if available, otherwise falls back to unitId.
    /// Search order: current version -> shared -> other versions (fallback).
    /// </summary>
    private static string? ResolveUnitGlbPath(
        string extractedDir,
        string? currentVersion,
        string unitId,
        string? faction,
        string? mesh)
    {
        if (!Directory.Exists(extractedDir))
            return null;

        var modelName = unitId;

        // Handle orphan GLBs with suffix (collision avoidance)
        if (unitId.EndsWith(OrphanGlbSuffix, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(mesh))
        {
            modelName = unitId.Substring(0, unitId.Length - OrphanGlbSuffix.Length);
        }
        else if (!string.IsNullOrEmpty(mesh))
        {
            var lastSlash = mesh.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < mesh.Length - 1)
            {
                modelName = mesh.Substring(lastSlash + 1);
            }
            else
            {
                modelName = mesh;
            }
        }

        // Priority 1: Current version directory
        if (!string.IsNullOrEmpty(currentVersion))
        {
            var currentVersionDir = Path.Combine(extractedDir, $"Assets-{currentVersion}");
            var result = SearchForUnitGlb(currentVersionDir, modelName, unitId, faction);
            if (result != null) return result;
        }

        // Priority 2: Shared directory
        var sharedDir = Path.Combine(extractedDir, "Assets-shared");
        var sharedResult = SearchForUnitGlb(sharedDir, modelName, unitId, faction);
        if (sharedResult != null) return sharedResult;

        // Priority 3: Other version directories (fallback)
        var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*")
            .Where(d => !d.EndsWith("Assets-shared") &&
                        (string.IsNullOrEmpty(currentVersion) || !d.EndsWith($"Assets-{currentVersion}")))
            .OrderByDescending(d => d); // Prefer newer versions

        foreach (var versionDir in versionDirs)
        {
            var result = SearchForUnitGlb(versionDir, modelName, unitId, faction);
            if (result != null) return result;
        }

        return null;
    }

    /// <summary>
    /// Searches for a unit GLB file in a specific version directory.
    /// </summary>
    private static string? SearchForUnitGlb(string versionDir, string modelName, string unitId, string? faction)
    {
        if (!Directory.Exists(versionDir))
            return null;

        // If we know the faction, try the specific path first with model name from mesh
        if (!string.IsNullOrEmpty(faction))
        {
            var specificPath = Path.Combine(versionDir, "Assets", "Resources", "units",
                faction.ToLowerInvariant(), $"{modelName.ToLowerInvariant()}.glb");
            if (File.Exists(specificPath))
                return specificPath;
        }

        var unitsDir = Path.Combine(versionDir, "Assets", "Resources", "units");
        if (Directory.Exists(unitsDir))
        {
            // First try with mesh-derived model name
            var glbFiles = Directory.GetFiles(unitsDir, $"{modelName.ToLowerInvariant()}.glb", SearchOption.AllDirectories);
            if (glbFiles.Length > 0)
                return glbFiles[0];

            if (modelName != unitId)
            {
                glbFiles = Directory.GetFiles(unitsDir, $"{unitId.ToLowerInvariant()}.glb", SearchOption.AllDirectories);
                if (glbFiles.Length > 0)
                    return glbFiles[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a map object GLB path by searching through ExtractedAssets directories.
    /// Path pattern: Assets-{version}/Assets/Resources/objects/{category}/{name}.glb
    /// Search order: current version -> shared -> other versions (fallback).
    /// </summary>
    private static string? ResolveMapObjectGlbPath(
        string extractedDir,
        string? currentVersion,
        string category,
        string name)
    {
        if (!Directory.Exists(extractedDir))
            return null;

        // Priority 1: Current version directory
        if (!string.IsNullOrEmpty(currentVersion))
        {
            var currentVersionDir = Path.Combine(extractedDir, $"Assets-{currentVersion}");
            var result = SearchForMapObjectGlb(currentVersionDir, category, name);
            if (result != null) return result;
        }

        // Priority 2: Shared directory
        var sharedDir = Path.Combine(extractedDir, "Assets-shared");
        var sharedResult = SearchForMapObjectGlb(sharedDir, category, name);
        if (sharedResult != null) return sharedResult;

        // Priority 3: Other version directories (fallback)
        var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*")
            .Where(d => !d.EndsWith("Assets-shared") &&
                        (string.IsNullOrEmpty(currentVersion) || !d.EndsWith($"Assets-{currentVersion}")))
            .OrderByDescending(d => d); // Prefer newer versions

        foreach (var versionDir in versionDirs)
        {
            var result = SearchForMapObjectGlb(versionDir, category, name);
            if (result != null) return result;
        }

        return null;
    }

    /// <summary>
    /// Searches for a map object GLB file in a specific version directory.
    /// </summary>
    private static string? SearchForMapObjectGlb(string versionDir, string category, string name)
    {
        if (!Directory.Exists(versionDir))
            return null;

        var objectPath = Path.Combine(versionDir, "Assets", "Resources", "objects",
            category.ToLowerInvariant(), $"{name.ToLowerInvariant()}.glb");
        return File.Exists(objectPath) ? objectPath : null;
    }

    /// <summary>
    /// Extracts the category from a map object ID (format: category/name).
    /// </summary>
    private static string? GetCategory(string id)
    {
        var slashIndex = id.IndexOf('/');
        return slashIndex > 0 ? id.Substring(0, slashIndex) : null;
    }

    /// <summary>
    /// Extracts the name from a map object ID (format: category/name).
    /// </summary>
    private static string GetName(string id)
    {
        var slashIndex = id.IndexOf('/');
        return slashIndex > 0 && slashIndex < id.Length - 1 ? id.Substring(slashIndex + 1) : id;
    }

    /// <summary>
    /// Gets the localized unit name from the resolver and lang index.
    /// Returns the raw SID being searched if localization is not found (for debugging missing translations).
    /// </summary>
    private static string? GetLocalizedUnitName(
        ITextResolver resolver,
        LangIndex lang,
        string unitId,
        string? locale)
    {
        // Try to get localized name from the unit's name SID
        var nameSid = $"{unitId}_name";

        // First try with resolver
        var name = TryResolveText(resolver, nameSid, locale);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        // Try direct lang lookup
        name = lang.ResolveText(nameSid);
        if (!string.IsNullOrWhiteSpace(name) && name != nameSid)
            return name;

        // Try with just unitId
        name = TryResolveText(resolver, unitId, locale);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        name = lang.ResolveText(unitId);
        if (!string.IsNullOrWhiteSpace(name) && name != unitId)
            return name;

        // Return the raw SID that was searched for (helps identify missing localizations)
        return nameSid;
    }

    /// <summary>
    /// Safely resolves a text SID, returning null on failure.
    /// </summary>
    private static string? TryResolveText(ITextResolver resolver, string sid, string? locale)
    {
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(locale))
            return null;

        try
        {
            var ctx = new ResolutionContext(locale);
            var result = resolver.Resolve(sid, ctx, out _);

            if (!string.IsNullOrWhiteSpace(result) && result != sid)
                return result;
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Checks if an artifact has localization (name OR description in lang files).
    /// </summary>
    private static bool HasArtifactLocalization(
        ArtifactsIndex.ArtifactRecord artifact,
        LangIndex lang)
    {
        var nameInLang = !string.IsNullOrWhiteSpace(artifact.NameSid)
            ? lang.ResolveText(artifact.NameSid)
            : null;
        var descInLang = !string.IsNullOrWhiteSpace(artifact.DescSid)
            ? lang.ResolveText(artifact.DescSid)
            : null;

        return nameInLang != null || descInLang != null;
    }

    /// <summary>
    /// Resolves the localized rarity + slot text for an artifact (e.g., "Legendary Armour").
    /// </summary>
    private static string? GetArtifactRaritySlotText(
        LangIndex lang,
        string? rarity,
        string? slot)
    {
        if (string.IsNullOrWhiteSpace(rarity) || string.IsNullOrWhiteSpace(slot))
            return null;

        var rarityLower = rarity.ToLowerInvariant();
        var slotNormalized = NormalizeSlotForSid(slot);
        var combinedSid = $"artifactRarity_{rarityLower}_{slotNormalized}";
        var raritySlotText = lang.ResolveText(combinedSid);

        // Future-proof fallback: try British spelling if American spelling not found
        if (raritySlotText == null && slotNormalized == "ARMOR")
        {
            var combinedSidBritish = $"artifactRarity_{rarityLower}_ARMOUR";
            raritySlotText = lang.ResolveText(combinedSidBritish);
        }

        return raritySlotText ?? $"{rarity} {slot}";
    }

    /// <summary>
    /// Normalizes artifact slot names for SID lookup.
    /// </summary>
    private static string NormalizeSlotForSid(string slot)
    {
        return slot.ToLowerInvariant().Replace(" ", "_") switch
        {
            "armour" or "armor" => "ARMOR",
            "back" => "BACK",
            "belt" => "BELT",
            "boots" => "BOOTS",
            "head" => "HEAD",
            "left_hand" or "main_hand" => "LEFT_HAND",
            "right_hand" or "off_hand" => "RIGHT_HAND",
            "ring" => "RING",
            "unique_slot" or "unic_slot" => "UNIQUE_SLOT",
            _ => slot.ToUpperInvariant().Replace(" ", "_")
        };
    }

    /// <summary>
    /// Deduplicates items by mesh/prefab path, selecting one representative per unique mesh.
    /// Prefers items whose ID matches the mesh name, then sorts alphabetically.
    /// </summary>
    /// <typeparam name="T">The type of items to deduplicate.</typeparam>
    private static List<T> DeduplicateByMesh<T>(
        IEnumerable<T> items,
        Func<T, string?> getMeshPath,
        Func<T, string> getId)
    {
        var itemsByMesh = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var meshPath = getMeshPath(item);
            if (string.IsNullOrEmpty(meshPath)) continue;

            var meshName = Path.GetFileName(meshPath);
            if (string.IsNullOrEmpty(meshName)) continue;

            if (!itemsByMesh.ContainsKey(meshName))
                itemsByMesh[meshName] = new List<T>();
            itemsByMesh[meshName].Add(item);
        }

        var deduplicated = new List<T>();
        foreach (var (meshName, meshItems) in itemsByMesh)
        {
            const int PreferredPriority = 0;
            const int FallbackPriority = 1;

            // Prefer items whose ID matches the mesh name for clearer naming
            var representative = meshItems
                .OrderBy(item => getId(item).Equals(meshName, StringComparison.OrdinalIgnoreCase)
                    ? PreferredPriority
                    : FallbackPriority)
                .ThenBy(item => getId(item))
                .First();
            deduplicated.Add(representative);
        }

        return deduplicated;
    }
}
