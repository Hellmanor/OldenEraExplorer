using Microsoft.AspNetCore.SignalR;
using API.Models;
using API.Services;

namespace API.Hubs;

public class ExtractionHub : Hub
{
    private readonly IAssetExtractionService _extractionService;
    private readonly ILogger<ExtractionHub> _logger;

    public ExtractionHub(
        IAssetExtractionService extractionService,
        ILogger<ExtractionHub> logger)
    {
        _extractionService = extractionService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected to ExtractionHub: {ConnectionId}", Context.ConnectionId);

        var status = _extractionService.GetStatus();
        await Clients.Caller.SendAsync("ExtractionStatus", status);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected from ExtractionHub: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public class ExtractionHubBroadcaster : IDisposable
{
    private readonly IHubContext<ExtractionHub> _hubContext;
    private readonly IAssetExtractionService _extractionService;
    private readonly ILogger<ExtractionHubBroadcaster> _logger;

    public ExtractionHubBroadcaster(
        IHubContext<ExtractionHub> hubContext,
        IAssetExtractionService extractionService,
        ILogger<ExtractionHubBroadcaster> logger)
    {
        _hubContext = hubContext;
        _extractionService = extractionService;
        _logger = logger;

        _extractionService.OnProgressChanged += OnProgressChanged;
        _extractionService.OnStatusChanged += OnStatusChanged;

        _logger.LogInformation("ExtractionHubBroadcaster initialized");
    }

    private void OnProgressChanged(ExtractionProgressDto progress)
    {
        _ = BroadcastProgressAsync(progress);
    }

    private void OnStatusChanged(string status)
    {
        _ = BroadcastStatusAsync(status);
    }

    private async Task BroadcastProgressAsync(ExtractionProgressDto progress)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ExtractionProgress", progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast extraction progress");
        }
    }

    private async Task BroadcastStatusAsync(string status)
    {
        try
        {
            var fullStatus = _extractionService.GetStatus();
            await _hubContext.Clients.All.SendAsync("ExtractionStatus", fullStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast extraction status");
        }
    }

    public void Dispose()
    {
        _extractionService.OnProgressChanged -= OnProgressChanged;
        _extractionService.OnStatusChanged -= OnStatusChanged;
    }
}
