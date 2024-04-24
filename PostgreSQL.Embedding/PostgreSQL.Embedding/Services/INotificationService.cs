using PostgreSQL.Embedding.Common.Models.Notification;

namespace PostgreSQL.Embedding.Services
{
    public interface INotificationService
    {
        Task Broadcast<TEvent>(TEvent @event) where TEvent : EventBase;
        Task SendTo<TEvent>(string userId, TEvent @event) where TEvent : EventBase;
    }
}
