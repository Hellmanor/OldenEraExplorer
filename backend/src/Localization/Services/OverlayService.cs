using System.Reflection;
using System.Text.Json;

namespace Localization.Services;

/// <summary>
/// Manages overlay text translations for UI labels (oe_* prefixed keys).
/// Loads embedded JSON resources and provides 3-tier localization resolution.
/// </summary>
public sealed class OverlayService
{
    private readonly Dictionary<string, Dictionary<string, string>> _overlays = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, string> _englishOverlay;

    private static readonly Lazy<OverlayService> _instance = new(() => new OverlayService());

    public static OverlayService Instance => _instance.Value;

    private OverlayService()
    {
        LoadOverlays();
        _englishOverlay = _overlays.TryGetValue("english", out var english)
            ? english
            : new Dictionary<string, string>();
    }

    public IReadOnlyDictionary<string, string>? GetOverlayForLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            return null;

        return _overlays.TryGetValue(locale, out var overlay) ? overlay : null;
    }

    public IReadOnlyDictionary<string, string> EnglishOverlay => _englishOverlay;

    public string? TryResolveFromOverlay(string sid, string locale)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return null;

        if (_overlays.TryGetValue(locale, out var localeOverlay) &&
            localeOverlay.TryGetValue(sid, out var text) &&
            !string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (_englishOverlay.TryGetValue(sid, out var englishText) &&
            !string.IsNullOrEmpty(englishText))
        {
            return englishText;
        }

        return null;
    }

    public bool HasOverlayKey(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return false;

        return _englishOverlay.ContainsKey(sid);
    }

    public IEnumerable<string> AvailableLocales => _overlays.Keys;

    private void LoadOverlays()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcePrefix = "Localization.Resources.Overlays.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(resourcePrefix) || !resourceName.EndsWith(".json"))
                continue;

            var locale = resourceName
                .Substring(resourcePrefix.Length)
                .Replace(".json", "");

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (dict != null)
                {
                    _overlays[locale] = dict;
                }
            }
            catch
            {
            }
        }
    }
}
