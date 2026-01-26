namespace PostgreSQL.Embedding.Domain.Models.Notification
{
    public class DocumentReadyEvent : EventBase
    {
        public string Content { get; set; }
    }
}
