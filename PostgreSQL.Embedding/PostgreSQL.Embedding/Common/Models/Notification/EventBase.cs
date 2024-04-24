namespace PostgreSQL.Embedding.Common.Models.Notification
{
    public class EventBase
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
