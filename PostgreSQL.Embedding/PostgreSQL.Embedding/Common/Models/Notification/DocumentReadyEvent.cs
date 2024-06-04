using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.Common.Models.Notification
{
    public class DocumentReadyEvent : EventBase
    {
        public string Content { get; set; }
    }
}
