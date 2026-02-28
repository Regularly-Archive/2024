using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities
{
    /// <summary>
    /// 推理过程
    /// </summary>
    [SugarTable("chat_message_reasonings")]
    public class ChatMessageReasoning : BaseEntity
    {
        /// <summary>
        /// 运行ID
        /// </summary>
        [SugarColumn(ColumnName = "run_id")]
        public string RunId { get; set; } = "";

        /// <summary>
        /// 消息ID
        /// </summary>
        [SugarColumn(ColumnName = "message_id")]
        public long MessageId { get; set; }

        /// <summary>
        /// 推理内容
        /// </summary>
        [SugarColumn(ColumnName = "content", ColumnDataType = "text")]
        public string Content { get; set; } = "";
    }
}
