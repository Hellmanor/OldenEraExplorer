using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using GameData.Shared.Utils;

namespace GameData.Indexing;

public sealed class MapObjectsIndex
{
    public sealed record MapObjectRecord(
        string Id,
        string Tag,
        bool IsInteractable,
        int SizeX,
        int SizeZ,
        string PrefabPath,
        string SourceFile,
        string NameSid,
        string DescriptionSid,
        string NarrativeDescriptionSid
    );

    private readonly Dictionary<string, MapObjectRecord> _mapObjects = new();
    public IReadOnlyDictionary<string, MapObjectRecord> MapObjects => _mapObjects;

    public void Scan(string streamingAssetsRoot)
    {
        _mapObjects.Clear();
        var zipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(zipPath)) return;

        using var zip = ZipFile.OpenRead(zipPath);

        // Include 6_artifacts.json for artifact model lookup
        var entries = zip.Entries.Where(e =>
            e.FullName.StartsWith("DB/map/objects/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            (e.FullName.EndsWith("3_resources.json", StringComparison.OrdinalIgnoreCase) ||
             e.FullName.EndsWith("4_interactables.json", StringComparison.OrdinalIgnoreCase) ||
             e.FullName.EndsWith("6_artifacts.json", StringComparison.OrdinalIgnoreCase)) &&
            e.Length > 0).ToList();

        foreach (var entry in entries)
        {
            try
            {
                ProcessMapObjectEntry(entry);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Trace($"[MapObjectsIndex] Error processing {entry.FullName}", ex);
            }
        }

        DiagnosticsLog.Trace($"[MapObjectsIndex] Loaded {_mapObjects.Count} map objects");
    }

    private void ProcessMapObjectEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var doc = JsonDocument.Parse(reader.ReadToEnd());

        if (!doc.RootElement.TryGetProperty("array", out var array))
            return;

        foreach (var el in array.EnumerateArray())
        {
            var id = el.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(id)) continue;

            // Cities are handled by BuildingsIndex
            if (id.EndsWith("_city", StringComparison.OrdinalIgnoreCase))
                continue;

            // Incomplete asset
            if (id.Equals("portal_magic", StringComparison.OrdinalIgnoreCase))
                continue;

            var lowerId = id.ToLowerInvariant();
            if (lowerId.StartsWith("custom_", StringComparison.Ordinal) ||
                lowerId.StartsWith("campaign_", StringComparison.Ordinal) ||
                lowerId.EndsWith("_campaign", StringComparison.Ordinal) ||
                lowerId.StartsWith("pvp_promo_", StringComparison.Ordinal))
                continue;

            // *_old items have unique GLBs, so keep them

            var tag = el.TryGetProperty("tag", out var tagP) ? tagP.GetString() ?? "" : "";
            var isInteractable = el.TryGetProperty("isInteractable", out var interP) && interP.ValueKind == JsonValueKind.True;

            var sizeX = 0;
            if (el.TryGetProperty("sizeX", out var sxP) && sxP.ValueKind == JsonValueKind.Number)
                sizeX = sxP.GetInt32();

            var sizeZ = 0;
            if (el.TryGetProperty("sizeZ", out var szP) && szP.ValueKind == JsonValueKind.Number)
                sizeZ = szP.GetInt32();

            var prefabPath = "";
            if (el.TryGetProperty("prefs", out var prefsP) && prefsP.ValueKind == JsonValueKind.Array)
            {
                prefabPath = prefsP.EnumerateArray()
                    .Select(p => p.GetString() ?? "")
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "";
            }

            if (!string.IsNullOrEmpty(prefabPath) && prefabPath.Contains("debug_objects/", StringComparison.OrdinalIgnoreCase))
                continue;

            // Keep only interactive/resource/barracks/artifact paths (artifact/ for ArtifactsIndex model lookup)
            if (!string.IsNullOrEmpty(prefabPath))
            {
                var lowerPath = prefabPath.ToLowerInvariant().Replace('\\', '/');
                if (!lowerPath.StartsWith("interactive/") &&
                    !lowerPath.StartsWith("resource/") &&
                    !lowerPath.StartsWith("barracks/") &&
                    !lowerPath.StartsWith("artifact/"))
                    continue;
            }

            var nameSid = el.TryGetProperty("name", out var nameP)
                ? nameP.GetString() ?? ""
                : "";

            var descriptionSid = el.TryGetProperty("description", out var descP)
                ? descP.GetString() ?? ""
                : "";

            var narrativeDescSid = el.TryGetProperty("narrativeDescription", out var narrativeP)
                ? narrativeP.GetString() ?? ""
                : "";

            if (!_mapObjects.ContainsKey(id))
            {
                _mapObjects[id] = new MapObjectRecord(
                    id,
                    tag,
                    isInteractable,
                    sizeX,
                    sizeZ,
                    prefabPath,
                    Path.GetFileName(entry.FullName),
                    nameSid,
                    descriptionSid,
                    narrativeDescSid
                );
            }
        }
    }
}
