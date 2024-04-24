using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Models.Notification;
using PostgreSQL.Embedding.Hubs;

namespace PostgreSQL.Embedding.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        public NotificationService(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task Broadcast<TEvent>(TEvent @event) where TEvent : EventBase
        {
            var serializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };
            var payload = JsonConvert.SerializeObject(@event, Formatting.Indented, serializerSettings);
            await _hubContext.Clients.All.SendAsync("Broadcast", payload);
        }

        public async Task SendTo<TEvent>(string userId, TEvent @event) where TEvent : EventBase
        {
            var serializerSettings = new JsonSerializerSettings 
            { 
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() 
            };
            var payload = JsonConvert.SerializeObject(@event, Formatting.Indented, serializerSettings);
            await _hubContext.Clients.Group(userId).SendAsync("Notification", payload);
        }
    }
}
