namespace AssetExtractor.Utilities;

/// <summary>
/// Global logging configuration.
/// Provides shared state for CLI components that need to adjust behavior based on logging settings.
/// </summary>
public static class LoggingConfig
{
    /// <summary>
    /// Indicates whether verbose (DEBUG level) logging is enabled.
    /// When true, components like ProgressBar disable their visual output to avoid cluttering verbose logs.
    /// </summary>
    public static bool IsVerboseMode { get; set; } = false;
}
