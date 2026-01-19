namespace AssetExtractor.Progress;

/// <summary>
/// Interface for reporting extraction progress.
/// Implementations can render to console, output JSON, or do nothing.
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Report progress update.
    /// </summary>
    /// <param name="phase">Current phase name (e.g., "Units", "Textures")</param>
    /// <param name="current">Current item index</param>
    /// <param name="total">Total number of items</param>
    /// <param name="currentItem">Optional: name of current item being processed</param>
    void Report(string phase, int current, int total, string? currentItem = null);

    /// <summary>
    /// Report an error message.
    /// </summary>
    void ReportError(string message);

    /// <summary>
    /// Mark the current phase as complete.
    /// </summary>
    /// <param name="phase">Phase name</param>
    /// <param name="message">Optional completion message</param>
    void Complete(string phase, string? message = null);

    /// <summary>
    /// Log an informational message.
    /// </summary>
    void Log(string message);
}
