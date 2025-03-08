using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Persistence.Hubs;

#region Hub
public class ProgressHub(ILogger<ProgressHub> _logger) : Hub
{
    #region Signal R Methods
    private const string RECEIVE_PROGRESS = "ReceiveProgress";
    private const string RECEIVE_ERROR = "ReceiveError";
    #endregion
    #region Log message templates
    private const string PROGRESS_ERROR = "Progress error: {Message}";
    private const string CLIENT_CONNECTED = "Client connected: {ConnectionId}";
    private const string PROGRESS_UPDATE = "Progress update: {Progress}/{Total}";
    private const string CLIENT_DISCONNECTED = "Client disconnected: {ConnectionId}";
    #endregion

    #region SignalR client methods
    public async Task SendProgress(double progress, double total, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(PROGRESS_UPDATE, progress, total);
        await Clients.All.SendAsync(RECEIVE_PROGRESS, progress, total, cancellationToken: cancellationToken);
    }

    public async Task SendError(string message)
    {
        _logger.LogError(PROGRESS_ERROR, message);
        await Clients.All.SendAsync(RECEIVE_ERROR, message);
    }
    #endregion

    #region SignalR Connection methods
    [ExcludeFromCodeCoverage]
    public override async Task OnConnectedAsync()
    {
        //_logger.LogInformation(CLIENT_CONNECTED, Context.ConnectionId);
        await Clients.All.SendAsync("UserConnected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }
    [ExcludeFromCodeCoverage]
    public override async Task OnDisconnectedAsync(Exception exception)
    {
        //_logger.LogInformation(CLIENT_DISCONNECTED, Context.ConnectionId);
        await Clients.All.SendAsync("UserDisconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
    #endregion
}
#endregion

#region Wrapper
public interface IProgressHubWrapper
{
    Task SendError(string message);
    Task SendProgress(double progress, double total, CancellationToken cancellationToken = default);
}

[ExcludeFromCodeCoverage]
public class ProgressHubWrapper(
    IHubContext<ProgressHub> _hubContext, 
    ILogger<IProgressHubWrapper> _logger
) : IProgressHubWrapper
{
    #region Log message templates
    private const string ReceiveErrorMethod = "ReceiveError";
    private const string ReceiveProgressMethod = "ReceiveProgress";
    private const string ProgressCompleteMessage = "Progress complete";
    private const string ProgressCompleteLogMessage = "Progress complete: {Message}";
    private const string ProgressUpdateMessage = "Progress update: {Progress}/{Total}";
    #endregion

    public async Task SendProgress(double progress, double total, CancellationToken cancellationToken = default) 
    {
        _logger.LogInformation(ProgressUpdateMessage, progress, total);

        if (progress.Equals(total)) 
            _logger.LogInformation(ProgressCompleteLogMessage, ProgressCompleteMessage);

        await _hubContext
            .Clients
            .All
            .SendAsync(
                ReceiveProgressMethod, 
                progress, 
                total,
                cancellationToken: cancellationToken
            );
    }

    public async Task SendError(string message) => await _hubContext.Clients.All.SendAsync(ReceiveErrorMethod, message);
}
#endregion