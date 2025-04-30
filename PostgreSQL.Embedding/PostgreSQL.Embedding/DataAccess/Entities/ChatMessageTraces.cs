using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("chat_message_traces")]
    public class ChatMessageTraces : BaseEntity
    {
        public long MessageId { get; set; }
    }
}
