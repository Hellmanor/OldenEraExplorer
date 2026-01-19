using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AssetExtractor.Utilities;

/// <summary>
/// Displays an animated spinner with elapsed time for long-running operations.
/// </summary>
public class SpinnerDisplay : IDisposable
{
    private readonly string _message;
    private readonly Stopwatch _stopwatch;
    private readonly CancellationTokenSource _cts;
    private readonly Task _spinnerTask;
    private int _lastLineLength;

    private static readonly char[] SpinnerChars = { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };

    public SpinnerDisplay(string message = "Loading")
    {
        _message = message;
        _stopwatch = Stopwatch.StartNew();
        _cts = new CancellationTokenSource();

        _spinnerTask = Task.Run(() => RunSpinner(_cts.Token));
    }

    private void RunSpinner(CancellationToken cancellationToken)
    {
        int spinnerIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            char spinnerChar = SpinnerChars[spinnerIndex];
            double elapsed = _stopwatch.Elapsed.TotalSeconds;
            string line = $"{spinnerChar} {_message}... {elapsed:F1}s";

            if (_lastLineLength > 0)
            {
                Console.Write("\r" + new string(' ', _lastLineLength) + "\r");
            }
            Console.Write(line);
            _lastLineLength = line.Length;

            spinnerIndex = (spinnerIndex + 1) % SpinnerChars.Length;
            Thread.Sleep(100);
        }
    }

    public void Complete(string completionMessage)
    {
        _cts.Cancel();
        _spinnerTask.Wait();
        _stopwatch.Stop();

        if (_lastLineLength > 0)
        {
            Console.Write("\r" + new string(' ', _lastLineLength) + "\r");
        }

        Console.WriteLine($"✓ {completionMessage}");
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
        _spinnerTask.Wait();
        _cts.Dispose();

        if (_lastLineLength > 0)
        {
            Console.Write("\r" + new string(' ', _lastLineLength) + "\r");
        }
    }
}
