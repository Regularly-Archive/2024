namespace PostgreSQL.Embedding.Domain.Models.Notification
{
    public class DocumentParsingStartedEvent : EventBase
    {
        public string Content { get; set; }
    }
}
