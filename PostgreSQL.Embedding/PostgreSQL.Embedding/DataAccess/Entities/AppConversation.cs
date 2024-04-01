using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("app_conversations")]
    public class AppConversation : BaseEntity
    {
        [SugarColumn(ColumnName = "app_id")]
        public long AppId { get; set; }

        [SugarColumn(ColumnName = "conversation_id")]
        public string ConversationId { get; set; }

        [SugarColumn(ColumnName = "summary", IsNullable = true)]
        public string Summary { get; set; }

    }
}
