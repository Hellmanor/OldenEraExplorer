#nullable enable
using System.Text;
using System.Text.Json;
using AssetExtractor.Models;

namespace AssetExtractor.Utilities;

/// <summary>
/// Simple console progress bar for visual feedback during extraction.
/// Thread-safe for parallel operations.
/// Supports optional callback for programmatic progress reporting.
/// Supports JSON output mode for subprocess integration.
/// </summary>
public class ProgressBar : IDisposable
{
    /// <summary>
    /// Subprocess integration via --json-progress CLI flag.
    /// </summary>
    public static bool JsonOutputMode { get; set; } = false;

    private readonly string _phaseName;
    private readonly int _total;
    private int _current;
    private int _lastRenderedLength;
    private readonly object _renderLock = new object();
    private readonly bool _enabled;
    private bool _completed;
    private readonly Action<ExtractionProgress>? _onProgress;
    private string? _currentItem;

    public ProgressBar(string phaseName, int total, Action<ExtractionProgress>? onProgress = null)
    {
        _phaseName = phaseName;
        _total = total;
        _current = 0;
        _lastRenderedLength = 0;
        _completed = false;
        _onProgress = onProgress;

        _enabled = !LoggingConfig.IsVerboseMode;

        if (_enabled && total > 0)
        {
            Render();
        }

        FireProgressCallback();
    }

    public void Update(int current, string? currentItem = null)
    {
        lock (_renderLock)
        {
            _current = current;
            _currentItem = currentItem;

            if (JsonOutputMode)
            {
                OutputJsonProgress();
            }
            else if (_enabled && _total > 0 && !_completed)
            {
                Render();
            }

            FireProgressCallback();
        }
    }

    private void FireProgressCallback()
    {
        _onProgress?.Invoke(new ExtractionProgressInfo(
            Phase: _phaseName,
            Current: _current,
            Total: _total,
            CurrentItem: _currentItem
        ));
    }

    /// <summary>
    /// Output progress as JSON line to stdout.
    /// Format: {"type":"progress","phase":"Units","current":50,"total":163,"percent":30.7,"item":"esquire"}
    /// </summary>
    private void OutputJsonProgress()
    {
        double percent = _total > 0 ? Math.Round((double)_current / _total * 100, 1) : 0;
        var json = new
        {
            type = "progress",
            phase = _phaseName,
            current = _current,
            total = _total,
            percent,
            item = _currentItem
        };
        Console.WriteLine(JsonSerializer.Serialize(json));
    }

    public void Complete()
    {
        lock (_renderLock)
        {
            _current = _total;
            _completed = true;

            if (JsonOutputMode)
            {
                OutputJsonProgress();
            }
            else if (_enabled && _total > 0)
            {
                Render();
                Console.WriteLine();
            }

            FireProgressCallback();
        }
    }

    private void Render()
    {
        if (!_enabled || _total == 0)
            return;

        if (_lastRenderedLength > 0)
        {
            Console.Write("\r" + new string(' ', _lastRenderedLength) + "\r");
        }

        const int barWidth = 30;
        int filled = Math.Min(barWidth, (int)((double)_current / _total * barWidth));

        var bar = new StringBuilder();
        bar.Append('[');

        for (int i = 0; i < barWidth; i++)
        {
            bar.Append(i < filled ? '█' : '░');
        }

        bar.Append("] ");

        double percentage = (double)_current / _total * 100;
        if (_completed)
        {
            bar.Append($"{_current}/{_total} (100%)");
        }
        else
        {
            bar.Append($"{percentage:F0}%");
            if (!string.IsNullOrEmpty(_currentItem))
            {
                bar.Append($" - {_phaseName}: {_currentItem}");
            }
        }

        string line = bar.ToString();
        Console.Write(line);
        _lastRenderedLength = line.Length;
    }

    public void Dispose()
    {
        if (JsonOutputMode || !_enabled || _total == 0)
            return;

        lock (_renderLock)
        {
            if (_lastRenderedLength > 0 && !_completed)
            {
                Console.WriteLine();
            }
        }
    }
}
