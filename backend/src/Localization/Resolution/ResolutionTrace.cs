namespace Localization.Resolution;

public sealed record ResolutionTrace(
    string Sid,
    IReadOnlyList<string> UsedSids,
    IReadOnlyList<string> UsedFunctions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors
);
