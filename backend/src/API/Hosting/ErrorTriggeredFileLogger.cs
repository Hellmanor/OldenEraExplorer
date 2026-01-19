using Microsoft.Extensions.Logging;

namespace API.Hosting;

/// <summary>
/// In-memory logger that dumps to file only when error occurs.
/// Avoids creating log files during normal operation, but preserves diagnostics when needed.
/// </summary>
public sealed class ErrorTriggeredFileLogger : ILoggerProvider
{
    private readonly CircularBuffer<LogEntry> _buffer = new(1000);
    private bool _errorOccurred = false;
    private StreamWriter? _fileWriter;
    private readonly string _logPath;
    private readonly object _lock = new();

    public ErrorTriggeredFileLogger()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", $"crash-{timestamp}.log");
    }

    public ILogger CreateLogger(string categoryName) => new BufferedLogger(this, categoryName);

    internal void Log(LogLevel level, string category, string message, Exception? exception)
    {
        var entry = new LogEntry(DateTime.Now, level, category, message, exception);

        lock (_lock)
        {
            _buffer.Add(entry);

            if (!_errorOccurred && (level >= LogLevel.Error))
            {
                _errorOccurred = true;
                FlushBufferToFile();
            }

            if (_errorOccurred && _fileWriter != null)
            {
                WriteToFile(entry);
            }
        }
    }

    private void FlushBufferToFile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            _fileWriter = new StreamWriter(_logPath, append: true) { AutoFlush = true };

            _fileWriter.WriteLine($"=== Error detected at {DateTime.Now:yyyy-MM-dd HH:mm:ss}, dumping buffer ({_buffer.Count} entries) ===");
            _fileWriter.WriteLine();

            foreach (var entry in _buffer.GetAll())
            {
                WriteToFile(entry);
            }

            _fileWriter.WriteLine();
            _fileWriter.WriteLine("=== Buffer dump complete, continuing real-time logging ===");
            _fileWriter.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create error log file: {ex.Message}");
        }
    }

    private void WriteToFile(LogEntry entry)
    {
        var levelStr = entry.Level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };

        _fileWriter?.WriteLine($"[{entry.Timestamp:HH:mm:ss} {levelStr}] [{entry.Category}] {entry.Message}");

        if (entry.Exception != null)
        {
            _fileWriter?.WriteLine(entry.Exception.ToString());
            _fileWriter?.WriteLine();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _fileWriter?.Dispose();
        }
    }

    private sealed class BufferedLogger : ILogger
    {
        private readonly ErrorTriggeredFileLogger _provider;
        private readonly string _categoryName;

        public BufferedLogger(ErrorTriggeredFileLogger provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter == null) return;

            var message = formatter(state, exception);
            _provider.Log(logLevel, _categoryName, message, exception);
        }
    }

    private record LogEntry(DateTime Timestamp, LogLevel Level, string Category, string Message, Exception? Exception);
}

internal sealed class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private int _index = 0;
    public int Count { get; private set; }

    public CircularBuffer(int capacity)
    {
        _buffer = new T[capacity];
    }

    public void Add(T item)
    {
        _buffer[_index] = item;
        _index = (_index + 1) % _buffer.Length;
        if (Count < _buffer.Length) Count++;
    }

    public IEnumerable<T> GetAll()
    {
        var start = Count < _buffer.Length ? 0 : _index;
        for (int i = 0; i < Count; i++)
        {
            yield return _buffer[(start + i) % _buffer.Length];
        }
    }
}
