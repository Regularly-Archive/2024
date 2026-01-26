using PostgreSQL.Embedding.Domain.Models.Notification;

namespace PostgreSQL.Embedding.Infrastructure.Messaging
{
    public interface INotificationService
    {
        Task Broadcast<TEvent>(TEvent @event) where TEvent : EventBase;
        Task SendTo<TEvent>(string userId, TEvent @event) where TEvent : EventBase;
    }
}
