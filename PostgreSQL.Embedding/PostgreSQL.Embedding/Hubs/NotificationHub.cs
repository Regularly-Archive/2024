using Microsoft.AspNetCore.SignalR;

namespace PostgreSQL.Embedding.Hubs
{
    public class NotificationHub : Hub
    {
        public ILogger<NotificationHub> _logger { get; set; }

        public NotificationHub(ILogger<NotificationHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"The user ({Context.User.Identity.Name}) connected with connectionId: {Context.ConnectionId}.");
            await Groups.AddToGroupAsync(Context.ConnectionId, Context.User.Identity.Name);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _logger.LogInformation($"The user ({Context.User.Identity.Name}) disconnected with connectionId: {Context.ConnectionId}.");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, Context.User.Identity.Name);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
