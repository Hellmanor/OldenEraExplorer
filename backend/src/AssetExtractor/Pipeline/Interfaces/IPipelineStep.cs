using AssetExtractor.Progress;

namespace AssetExtractor.Pipeline.Interfaces;

/// <summary>
/// Interface for extraction pipeline steps.
/// Each step represents a discrete unit of work in the extraction process.
/// </summary>
public interface IPipelineStep
{
    /// <summary>
    /// Step name for display and logging.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Execute the pipeline step.
    /// </summary>
    /// <param name="context">Pipeline execution context.</param>
    /// <param name="progress">Progress reporter for UI updates.</param>
    /// <param name="cancellationToken">Cancellation token for aborting.</param>
    /// <returns>Result of step execution.</returns>
    Task<StepResult> ExecuteAsync(
        PipelineContext context,
        IProgressReporter progress,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a pipeline step execution.
/// </summary>
public record StepResult(
    bool Success,
    string? Message = null,
    int? ProcessedCount = null,
    int? FailedCount = null);

/// <summary>
/// Context shared across pipeline steps.
/// </summary>
public record PipelineContext
{
    /// <summary>
    /// Path to game installation directory.
    /// </summary>
    public required string GamePath { get; init; }

    /// <summary>
    /// Path for extracted asset output.
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Force re-extraction even if cached.
    /// </summary>
    public bool Force { get; init; }

    /// <summary>
    /// Detected game version (set during initialization).
    /// </summary>
    public string? Version { get; set; }
}
