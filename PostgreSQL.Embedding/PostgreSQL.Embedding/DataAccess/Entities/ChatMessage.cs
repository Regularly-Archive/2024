using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("chat_messages")]
    public class ChatMessage : BaseEntity
    {
        [SugarColumn(ColumnName = "app_id")]
        public long AppId { get; set; }

        [SugarColumn(ColumnName = "conversation_id")]
        public string ConversationId { get; set; }

        [SugarColumn(ColumnName = "content", ColumnDataType = "text")]
        public string Content { get; set; }

        [SugarColumn(ColumnName ="is_user_message", ColumnDataType = "boolean")]
        public bool IsUserMessage { get; set; }

    }
}
