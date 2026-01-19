namespace Localization.Resolution;

public interface ITextResolver
{
    string Resolve(string sid, ResolutionContext ctx, out ResolutionTrace trace);
}
