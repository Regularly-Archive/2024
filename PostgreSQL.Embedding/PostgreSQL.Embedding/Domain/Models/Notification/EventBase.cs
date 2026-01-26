namespace PostgreSQL.Embedding.Domain.Models.Notification
{
    public class EventBase
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
