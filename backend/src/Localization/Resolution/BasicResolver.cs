using Localization.Indexing;
using Localization.Services;

namespace Localization.Resolution;

/// <summary>
/// Text lookup without placeholder substitution - for when placeholder resolution is disabled.
/// </summary>
public sealed class BasicResolver : ITextResolver
{
    private readonly LangIndex _langIndex;

    public BasicResolver(LangIndex langIndex)
    {
        _langIndex = langIndex;
    }

    public string Resolve(string sid, ResolutionContext ctx, out ResolutionTrace trace)
    {
        var usedSids = new List<string> { sid };
        var warnings = new List<string>();

        var overlayResult = OverlayService.Instance.TryResolveFromOverlay(sid, ctx.Locale);
        if (overlayResult != null)
        {
            trace = new ResolutionTrace(sid, usedSids, Array.Empty<string>(), warnings, Array.Empty<string>());
            return overlayResult;
        }

        var text = _langIndex.ResolveText(sid);
        if (text != null)
        {
            trace = new ResolutionTrace(sid, usedSids, Array.Empty<string>(), warnings, Array.Empty<string>());
            return text;
        }
        warnings.Add($"SID not found: {sid}");
        trace = new ResolutionTrace(sid, usedSids, Array.Empty<string>(), warnings, Array.Empty<string>());
        return sid;
    }
}
