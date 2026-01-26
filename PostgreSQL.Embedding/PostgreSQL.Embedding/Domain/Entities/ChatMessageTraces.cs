using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities
{
    [SugarTable("chat_message_traces")]
    public class ChatMessageTraces : BaseEntity
    {
        public long MessageId { get; set; }
    }
}
