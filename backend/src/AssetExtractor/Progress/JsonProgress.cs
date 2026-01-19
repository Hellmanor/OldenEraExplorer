using System.Text.Json;

namespace AssetExtractor.Progress;

/// <summary>
/// JSON-based progress reporter for subprocess mode.
/// Outputs JSON lines to stdout for parsing by parent process.
/// </summary>
public sealed class JsonProgress : IProgressReporter
{
    private readonly object _writeLock = new();

    public void Report(string phase, int current, int total, string? currentItem = null)
    {
        lock (_writeLock)
        {
            double percent = total > 0 ? Math.Round((double)current / total * 100, 1) : 0;
            var json = new
            {
                type = "progress",
                phase,
                current,
                total,
                percent,
                item = currentItem
            };
            Console.WriteLine(JsonSerializer.Serialize(json));
        }
    }

    public void ReportError(string message)
    {
        lock (_writeLock)
        {
            var json = new
            {
                type = "error",
                message
            };
            Console.WriteLine(JsonSerializer.Serialize(json));
        }
    }

    public void Complete(string phase, string? message = null)
    {
        lock (_writeLock)
        {
            var json = new
            {
                type = "complete",
                phase,
                message
            };
            Console.WriteLine(JsonSerializer.Serialize(json));
        }
    }

    public void Log(string message)
    {
        lock (_writeLock)
        {
            var json = new
            {
                type = "log",
                message
            };
            Console.WriteLine(JsonSerializer.Serialize(json));
        }
    }
}
