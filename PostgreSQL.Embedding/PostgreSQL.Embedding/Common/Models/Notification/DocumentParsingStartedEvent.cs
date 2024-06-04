namespace PostgreSQL.Embedding.Common.Models.Notification
{
    public class DocumentParsingStartedEvent : EventBase
    {
        public string Content { get; set; }
    }
}
