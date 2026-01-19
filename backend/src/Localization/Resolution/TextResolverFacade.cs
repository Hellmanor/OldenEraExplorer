namespace Localization.Resolution;

public sealed class TextResolverFacade : ITextResolver
{
    private readonly ITextResolver _inner;

    public TextResolverFacade(ITextResolver inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string Resolve(string sid, ResolutionContext ctx, out ResolutionTrace trace)
        => _inner.Resolve(sid, ctx, out trace);

    public string Resolve(string sid, string locale)
        => _inner.Resolve(sid, new ResolutionContext(locale), out _);

    public string Resolve(string sid, ResolutionContext ctx)
        => _inner.Resolve(sid, ctx, out _);
}
