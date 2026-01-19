using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GameData.Indexing;
using Localization.DbAccess;
using Localization.Indexing;
using Localization.Resolution;

namespace GameData.Details;

public class MapObjectDetailsService
{
    private readonly DbAccessor _dbAccessor;
    private readonly ITextResolver _textResolver;
    private readonly ILogger<MapObjectDetailsService> _logger;

    public MapObjectDetailsService(
        DbAccessor dbAccessor,
        ITextResolver textResolver,
        ILogger<MapObjectDetailsService> logger)
    {
        _dbAccessor = dbAccessor ?? throw new ArgumentNullException(nameof(dbAccessor));
        _textResolver = textResolver ?? throw new ArgumentNullException(nameof(textResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MapObjectDetailsDto> GetDetailsAsync(
        string mapObjectId,
        string streamingAssetsRoot,
        string locale,
        LangIndex lang,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mapObjectId))
            throw new ArgumentException("Map object ID cannot be null or whitespace.", nameof(mapObjectId));
        if (string.IsNullOrWhiteSpace(streamingAssetsRoot))
            throw new ArgumentException("Streaming assets root cannot be null or whitespace.", nameof(streamingAssetsRoot));
        if (string.IsNullOrWhiteSpace(locale))
            throw new ArgumentException("Locale cannot be null or whitespace.", nameof(locale));
        if (lang == null)
            throw new ArgumentNullException(nameof(lang));

        cancellationToken.ThrowIfCancellationRequested();

        var mapObjectsIndex = new MapObjectsIndex();
        mapObjectsIndex.Scan(streamingAssetsRoot);

        if (!mapObjectsIndex.MapObjects.TryGetValue(mapObjectId, out var mapObject))
        {
            _logger.LogWarning("Map object {MapObjectId} not found in index", mapObjectId);
            throw new InvalidOperationException($"Map object '{mapObjectId}' not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var ctx = new ResolutionContext(locale);

        var nameSid = !string.IsNullOrWhiteSpace(mapObject.NameSid)
            ? mapObject.NameSid
            : $"{mapObject.Id}_name";
        var descSid = !string.IsNullOrWhiteSpace(mapObject.DescriptionSid)
            ? mapObject.DescriptionSid
            : $"{mapObject.Id}_description";

        var nameInLang = lang.ResolveText(nameSid);
        var name = nameInLang != null
            ? (_textResolver.Resolve(nameSid, ctx, out _) ?? nameInLang)
            : mapObject.Id;

        var descInLang = lang.ResolveText(descSid);
        var description = descInLang != null
            ? (_textResolver.Resolve(descSid, ctx, out _) ?? descInLang)
            : "";

        var narrativeDescription = "";
        if (!string.IsNullOrEmpty(mapObject.NarrativeDescriptionSid))
        {
            narrativeDescription = _textResolver.Resolve(mapObject.NarrativeDescriptionSid, ctx, out _)
                ?? lang.ResolveText(mapObject.NarrativeDescriptionSid) ?? "";
        }

        if (string.IsNullOrEmpty(narrativeDescription))
        {
            var narrativeSid = $"{mapObject.Id}_narrativeDescription";
            narrativeDescription = _textResolver.Resolve(narrativeSid, ctx, out _)
                ?? lang.ResolveText(narrativeSid) ?? "";
        }

        var iconPath = "";
        if (!string.IsNullOrWhiteSpace(mapObject.PrefabPath))
        {
            iconPath = mapObject.PrefabPath.Replace('\\', '/');
            if (iconPath.StartsWith("objects/", StringComparison.OrdinalIgnoreCase))
                iconPath = iconPath.Substring("objects/".Length);
        }

        return await Task.FromResult(new MapObjectDetailsDto(
            mapObjectId,
            name,
            iconPath,
            description,
            narrativeDescription,
            mapObject.Tag ?? string.Empty,
            mapObject.IsInteractable,
            mapObject.SizeX,
            mapObject.SizeZ,
            mapObject.SourceFile));
    }
}

public record MapObjectDetailsDto(
    string MapObjectId,
    string Name,
    string Icon,
    string Description,
    string NarrativeDescription,
    string Tag,
    bool IsInteractable,
    int SizeX,
    int SizeZ,
    string SourceFile);
