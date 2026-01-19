namespace AssetExtractor.Progress;

/// <summary>
/// Null progress reporter - performs no output.
/// Useful for library usage or testing.
/// </summary>
public sealed class NullProgress : IProgressReporter
{
    public static readonly NullProgress Instance = new();

    public void Report(string phase, int current, int total, string? currentItem = null) { }
    public void ReportError(string message) { }
    public void Complete(string phase, string? message = null) { }
    public void Log(string message) { }
}
