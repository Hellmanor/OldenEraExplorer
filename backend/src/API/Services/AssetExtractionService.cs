using System.Diagnostics;
using System.Text.Json;
using AssetExtractor.Pipeline;
using API.Models;

namespace API.Services;

public record ExtractionJob(
    string JobId,
    bool ExtractPng,
    bool ExtractGlb,
    bool Force,
    string GamePath,
    string OutputPath,
    CancellationToken CancellationToken
);

internal record CliProgressMessage(
    string? Type,
    string? Phase,
    int Current,
    int Total,
    double Percent,
    string? Item,
    string? Status,
    int? Success,
    int? Failed,
    string? Message,
    List<string>? FailedItems
);

/// <summary>
/// Uses the CLI as a subprocess for extraction - enables true cancellation via Process.Kill().
/// Cache checking is done in-process for instant response.
/// </summary>
public class AssetExtractionService : IAssetExtractionService, IDisposable
{
    private readonly IGamePathService _gamePathService;
    private readonly IAssetServingService _assetServingService;
    private readonly ILogger<AssetExtractionService> _logger;
    private readonly string _outputPath;
    private readonly string _executablePath;
    private readonly ManifestManager _manifestService;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private string? _currentJobId;
    private string _status = "Idle";
    private ExtractionProgressDto? _currentProgress;
    private Stopwatch? _stopwatch;
    private Task? _extractionTask;
    private Process? _currentProcess;

    private DateTime _lastBroadcast = DateTime.MinValue;
    private ExtractionProgressDto? _pendingProgress;
    private readonly TimeSpan _throttleInterval = TimeSpan.FromMilliseconds(100);

    public bool IsRunning => _status == "Extracting";

    public event Action<ExtractionProgressDto>? OnProgressChanged;
    public event Action<string>? OnStatusChanged;

    public AssetExtractionService(
        IGamePathService gamePathService,
        IAssetServingService assetServingService,
        ILogger<AssetExtractionService> logger)
    {
        _gamePathService = gamePathService;
        _assetServingService = assetServingService;
        _logger = logger;

        _outputPath = Path.Combine(AppContext.BaseDirectory, "ExtractedAssets");
        Directory.CreateDirectory(_outputPath);
        _executablePath = FindExecutablePath();
        _manifestService = new ManifestManager(_outputPath);
        _gamePathService.PathChanged += OnPathChanged;

        if (_gamePathService.IsPathSet && _gamePathService.HeroesOeDataPath != null)
        {
            UpdateCurrentVersion(_gamePathService.HeroesOeDataPath);
        }

        _logger.LogInformation("AssetExtractionService initialized (subprocess mode). Output path: {OutputPath}", _outputPath);
        _logger.LogInformation("Executable path: {ExePath}", _executablePath);
    }

    private static string FindExecutablePath()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
        {
            return exePath;
        }

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "OldenEraExplorer"),
            Path.Combine(baseDir, "OldenEraExplorer.exe"),
            Path.Combine(baseDir, "OldenEraExplorer.dll")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not find executable. ProcessPath: {exePath}, BaseDirectory: {baseDir}");
    }

    private void OnPathChanged(object? sender, GamePathChangedEventArgs e)
    {
        if (e.IsCleared)
        {
            _assetServingService.SetCurrentVersion(null);
            _logger.LogInformation("Game path cleared, version reset");
        }
        else if (e.HeroesOeDataPath != null)
        {
            UpdateCurrentVersion(e.HeroesOeDataPath);
        }

        OnStatusChanged?.Invoke(_status);
    }

    private void UpdateCurrentVersion(string heroesOeDataPath)
    {
        try
        {
            var version = ManifestManager.DetectGameVersion(heroesOeDataPath);
            _assetServingService.SetCurrentVersion(version);
            _logger.LogInformation("Detected and set game version: {Version}", version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect game version, using fallback");
            _assetServingService.SetCurrentVersion(null);
        }
    }

    public string StartExtraction(StartExtractionRequest request)
    {
        lock (_lock)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("Extraction is already running");
            }

            var gamePath = _gamePathService.HeroesOeDataPath;
            if (string.IsNullOrEmpty(gamePath))
            {
                throw new InvalidOperationException("Game path not configured. Please set the game path first.");
            }

            if (!request.ForceReExtract)
            {
                try
                {
                    var version = ManifestManager.DetectGameVersion(gamePath);
                    if (_manifestService.IsVersionExtracted(version))
                    {
                        _logger.LogInformation("Cache hit for version {Version}, skipping extraction", version);

                        var cacheHitJobId = Guid.NewGuid().ToString("N")[..8];

                        Task.Run(() =>
                        {
                            OnStatusChanged?.Invoke("Completed");
                        });

                        return cacheHitJobId;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check cache, proceeding with extraction");
                }
            }

            _currentJobId = Guid.NewGuid().ToString("N")[..8];
            _cts = new CancellationTokenSource();
            _stopwatch = Stopwatch.StartNew();

            var job = new ExtractionJob(
                JobId: _currentJobId,
                ExtractPng: request.ExtractPng,
                ExtractGlb: request.ExtractGlb,
                Force: request.ForceReExtract,
                GamePath: gamePath,
                OutputPath: _outputPath,
                CancellationToken: _cts.Token
            );

            _currentProgress = new ExtractionProgressDto(
                JobId: _currentJobId,
                Phase: "Initializing",
                Current: 0,
                Total: 0,
                Percent: 0,
                CurrentAsset: "Starting CLI process...",
                Elapsed: "00:00",
                EstimatedRemaining: null
            );
            SetStatus("Extracting");

            _logger.LogInformation("Starting extraction job {JobId} (PNG: {Png}, GLB: {Glb}, Force: {Force})",
                _currentJobId, request.ExtractPng, request.ExtractGlb, request.ForceReExtract);

            _extractionTask = Task.Run(() => ExecuteCliExtractionAsync(job), _cts.Token);

            return _currentJobId;
        }
    }

    private static List<string> MapToExtractionArgs(ExtractionJob job)
    {
        var args = new List<string>
        {
            "--extract",
            "--game-path", job.GamePath,
            "--output-path", job.OutputPath
        };

        if (job.ExtractPng && job.ExtractGlb)
        {
            args.Add("--all");
        }
        else if (job.ExtractGlb)
        {
            args.Add("--glb");
        }
        else if (job.ExtractPng)
        {
            args.Add("--png");
        }
        else
        {
            throw new InvalidOperationException("At least one of ExtractPng or ExtractGlb must be true");
        }

        if (job.Force)
        {
            args.Add("--force");
        }

        return args;
    }

    private async Task ExecuteCliExtractionAsync(ExtractionJob job)
    {
        try
        {
            _logger.LogInformation("Extraction job {JobId} started (subprocess). Game path: {GamePath}", job.JobId, job.GamePath);

            var extractionArgs = MapToExtractionArgs(job);
            _logger.LogInformation("Extraction args: {Args}", string.Join(" ", extractionArgs));

            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                WorkingDirectory = Path.GetDirectoryName(_executablePath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in extractionArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            _logger.LogDebug("Starting process: {Exe} {Args}", _executablePath, string.Join(" ", startInfo.ArgumentList));

            using var process = new Process { StartInfo = startInfo };

            lock (_lock)
            {
                _currentProcess = process;
            }

            process.Start();

            var stdoutTask = ReadStdoutAsync(process.StandardOutput, job.JobId, job.CancellationToken);
            var stderrTask = ReadStderrAsync(process.StandardError, job.CancellationToken);

            try
            {
                await process.WaitForExitAsync(job.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Cancellation requested, killing CLI process");
                try
                {
                    // Subprocess cancellation: kill entire process tree to ensure extraction stops
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to kill CLI process");
                }
                throw;
            }

            await Task.WhenAll(stdoutTask, stderrTask);

            var exitCode = process.ExitCode;
            _logger.LogInformation("CLI process exited with code {ExitCode}", exitCode);

            FlushPendingProgress();
            _manifestService.ReloadManifest();

            if (job.CancellationToken.IsCancellationRequested)
            {
                SetStatus("Cancelled");
                _logger.LogInformation("Extraction job {JobId} was cancelled", job.JobId);
            }
            else
            {
                SetStatus(exitCode == 0 ? "Completed" : "Completed with errors");

                var gamePath = _gamePathService.HeroesOeDataPath;
                if (!string.IsNullOrEmpty(gamePath))
                {
                    UpdateCurrentVersion(gamePath);
                    _logger.LogInformation("Current version updated after extraction");
                }

                // Cache invalidation: force asset lookup refresh after extraction completes
                _assetServingService.InvalidateCache();
                _logger.LogInformation("Asset cache invalidated after extraction");

                _logger.LogInformation("Extraction job {JobId} completed with exit code {ExitCode}", job.JobId, exitCode);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled");
            _logger.LogInformation("Extraction job {JobId} was cancelled", job.JobId);
        }
        catch (Exception ex)
        {
            SetStatus("Failed");
            _logger.LogError(ex, "Extraction job {JobId} failed", job.JobId);

            lock (_lock)
            {
                if (_currentProgress != null)
                {
                    _currentProgress = _currentProgress with
                    {
                        CurrentAsset = $"Error: {ex.Message}"
                    };
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                _stopwatch?.Stop();
                _currentProcess = null;
            }
        }
    }

    private async Task ReadStdoutAsync(StreamReader reader, string jobId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                ParseJsonProgress(line, jobId);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading CLI stdout");
        }
    }

    private async Task ReadStderrAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogWarning("CLI stderr: {Line}", line);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading CLI stderr");
        }
    }

    private void ParseJsonProgress(string line, string jobId)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var message = JsonSerializer.Deserialize<CliProgressMessage>(line, options);

            if (message == null)
                return;

            switch (message.Type?.ToLower())
            {
                case "progress":
                    UpdateProgress(jobId, message);
                    break;

                case "status":
                    _logger.LogInformation("CLI status: {Status} (success: {Success}, failed: {Failed})",
                        message.Status, message.Success, message.Failed);
                    break;

                case "error":
                    _logger.LogError("CLI error: {Message}", message.Message);
                    break;

                default:
                    _logger.LogDebug("CLI output: {Line}", line);
                    break;
            }
        }
        catch (JsonException)
        {
            if (line.StartsWith("[") || line.Contains("Error") || line.Contains("Warning"))
            {
                _logger.LogDebug("CLI output: {Line}", line);
            }
        }
    }

    private void UpdateProgress(string jobId, CliProgressMessage message)
    {
        ExtractionProgressDto progress;

        lock (_lock)
        {
            if (_currentJobId != jobId)
                return;

            var elapsed = _stopwatch?.Elapsed ?? TimeSpan.Zero;
            var elapsedStr = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"hh\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");

            string? estimatedRemaining = null;
            if (message.Current > 0 && message.Total > 0)
            {
                var progressRatio = (double)message.Current / message.Total;
                if (progressRatio > 0.01)
                {
                    var estimatedTotal = elapsed / progressRatio;
                    var remaining = estimatedTotal - elapsed;
                    if (remaining > TimeSpan.Zero)
                    {
                        estimatedRemaining = remaining.TotalHours >= 1
                            ? remaining.ToString(@"hh\:mm\:ss")
                            : remaining.ToString(@"mm\:ss");
                    }
                }
            }

            progress = new ExtractionProgressDto(
                JobId: jobId,
                Phase: message.Phase ?? "Processing",
                Current: message.Current,
                Total: message.Total,
                Percent: message.Percent,
                CurrentAsset: message.Item ?? "Processing...",
                Elapsed: elapsedStr,
                EstimatedRemaining: estimatedRemaining
            );

            _currentProgress = progress;
        }

        ThrottledBroadcast(progress);
    }

    private void ThrottledBroadcast(ExtractionProgressDto progress)
    {
        lock (_lock)
        {
            _pendingProgress = progress;
            var now = DateTime.UtcNow;

            if (now - _lastBroadcast >= _throttleInterval)
            {
                _lastBroadcast = now;
                var toSend = _pendingProgress;
                _pendingProgress = null;

                Task.Run(() => OnProgressChanged?.Invoke(toSend));
            }
        }
    }

    private void FlushPendingProgress()
    {
        ExtractionProgressDto? toSend;
        lock (_lock)
        {
            toSend = _pendingProgress;
            _pendingProgress = null;
        }

        if (toSend != null)
        {
            OnProgressChanged?.Invoke(toSend);
        }
    }

    public void CancelExtraction()
    {
        lock (_lock)
        {
            if (!IsRunning)
            {
                _logger.LogWarning("Cancel requested but no extraction is running");
                return;
            }

            _logger.LogInformation("Cancellation requested for job {JobId}", _currentJobId);
            _cts?.Cancel();

            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _logger.LogInformation("Killing CLI process (PID: {Pid})", _currentProcess.Id);
                    // Subprocess cancellation: kill entire process tree to ensure extraction stops
                    _currentProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill CLI process during cancellation");
            }
        }
    }

    public ExtractionStatusDto GetStatus()
    {
        lock (_lock)
        {
            var (lastExtractedAt, iconCount, modelCount, gameVersion) = GetManifestInfo();
            return new ExtractionStatusDto(_status, _currentProgress, lastExtractedAt, iconCount, modelCount, gameVersion);
        }
    }

    private (DateTime? LastExtractedAt, int IconCount, int ModelCount, string? GameVersion) GetManifestInfo()
    {
        try
        {
            var currentVersion = _assetServingService.CurrentVersion;
            var manifestData = _manifestService.Manifest;

            DateTime? extractedAt = null;
            string? gameVersion = currentVersion;
            int iconCount = 0;
            int modelCount = 0;

            if (!string.IsNullOrEmpty(currentVersion) && manifestData.Builds.TryGetValue(currentVersion, out var buildInfo))
            {
                if (DateTime.TryParse(buildInfo.ExtractedAt, out var parsed))
                {
                    extractedAt = parsed;
                }
            }
            else
            {
                foreach (var build in manifestData.Builds)
                {
                    if (DateTime.TryParse(build.Value.ExtractedAt, out var parsed))
                    {
                        if (extractedAt == null || parsed > extractedAt)
                        {
                            extractedAt = parsed;
                            gameVersion = build.Key;
                        }
                    }
                }
            }

            foreach (var asset in manifestData.Assets)
            {
                if (asset.Value.Extension == ".png") iconCount++;
                else if (asset.Value.Extension == ".glb") modelCount++;
            }

            return (extractedAt, iconCount, modelCount, gameVersion);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read manifest info");
            return (null, 0, 0, null);
        }
    }

    private void SetStatus(string status)
    {
        lock (_lock)
        {
            _status = status;
        }

        _logger.LogDebug("Extraction status changed to: {Status}", status);
        OnStatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        _gamePathService.PathChanged -= OnPathChanged;
        _cts?.Cancel();
        try
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                // Subprocess cleanup: kill entire process tree on disposal
                _currentProcess.Kill(entireProcessTree: true);
            }
        }
        catch { }
        _cts?.Dispose();
    }
}
