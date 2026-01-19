using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AssetExtractor.Utilities;

/// <summary>
/// Disposable scope for automatic timing and logging of operations.
/// Measures elapsed time from construction to disposal and logs the result.
/// </summary>
/// <example>
/// <code>
/// using (new TimingScope(_logger, "Unit Extraction"))
/// {
///     // ... perform extraction work
/// }
/// // Automatically logs: "Unit Extraction completed in 12.3s"
/// </code>
/// </example>
public sealed class TimingScope : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _operationName;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    public TimingScope(ILogger logger, string operationName)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
        _stopwatch = Stopwatch.StartNew();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stopwatch.Stop();

        _logger.LogInformation(
            "{OperationName} completed in {ElapsedSeconds:F1}s",
            _operationName,
            _stopwatch.Elapsed.TotalSeconds);
    }
}
