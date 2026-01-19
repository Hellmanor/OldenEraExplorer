using System.Text;

namespace AssetExtractor.Progress;

/// <summary>
/// Console-based progress reporter with progress bar rendering.
/// Thread-safe for parallel operations.
/// </summary>
public sealed class ConsoleProgress : IProgressReporter
{
    private readonly object _renderLock = new();
    private readonly bool _verbose;
    private int _lastRenderedLength;
    private string? _currentPhase;

    public ConsoleProgress(bool verbose = false)
    {
        _verbose = verbose;
    }

    public void Report(string phase, int current, int total, string? currentItem = null)
    {
        lock (_renderLock)
        {
            // If phase changed, finish previous line
            if (_currentPhase != null && _currentPhase != phase)
            {
                Console.WriteLine();
                _lastRenderedLength = 0;
            }
            _currentPhase = phase;

            if (_verbose)
            {
                // In verbose mode, just log each item
                if (currentItem != null)
                {
                    Console.WriteLine($"[{phase}] {current}/{total}: {currentItem}");
                }
            }
            else
            {
                RenderProgressBar(phase, current, total);
            }
        }
    }

    public void ReportError(string message)
    {
        lock (_renderLock)
        {
            // Move to new line if we were rendering a progress bar
            if (_lastRenderedLength > 0)
            {
                Console.WriteLine();
                _lastRenderedLength = 0;
            }

            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {message}");
            Console.ForegroundColor = oldColor;
        }
    }

    public void Complete(string phase, string? message = null)
    {
        lock (_renderLock)
        {
            if (!_verbose && _lastRenderedLength > 0)
            {
                Console.WriteLine();
                _lastRenderedLength = 0;
            }

            if (message != null)
            {
                var oldColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ {message}");
                Console.ForegroundColor = oldColor;
            }

            _currentPhase = null;
        }
    }

    public void Log(string message)
    {
        lock (_renderLock)
        {
            // Move to new line if we were rendering a progress bar
            if (_lastRenderedLength > 0)
            {
                Console.WriteLine();
                _lastRenderedLength = 0;
            }
            Console.WriteLine(message);
        }
    }

    private void RenderProgressBar(string phase, int current, int total)
    {
        if (total == 0) return;

        // Clear previous line
        if (_lastRenderedLength > 0)
        {
            Console.Write("\r" + new string(' ', _lastRenderedLength) + "\r");
        }

        // Build progress bar
        const int barWidth = 30;
        int filled = Math.Min(barWidth, (int)((double)current / total * barWidth));

        var bar = new StringBuilder();
        bar.Append(phase);
        bar.Append(": [");

        for (int i = 0; i < barWidth; i++)
        {
            bar.Append(i < filled ? '=' : '-');
        }

        bar.Append("] ");

        double percentage = (double)current / total * 100;
        bar.Append($"{current}/{total} ({percentage:F0}%)");

        string line = bar.ToString();
        Console.Write(line);
        _lastRenderedLength = line.Length;
    }
}
