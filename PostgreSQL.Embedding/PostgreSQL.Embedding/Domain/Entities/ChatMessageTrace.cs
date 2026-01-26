using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities
{
    [SugarTable("chat_message_traces")]
    public class ChatMessageTrace : BaseEntity
    {
        public long MessageId { get; set; }
        public int TraceType { get; set; }
    }
}
