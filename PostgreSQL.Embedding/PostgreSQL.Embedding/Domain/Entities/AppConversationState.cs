using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities
{
    /// <summary>
    /// 会话压缩状态
    /// </summary>
    [SugarTable("app_conversation_states")]
    public class AppConversationState : BaseEntity
    {
        /// <summary>
        /// 会话ID
        /// </summary>
        [SugarColumn(ColumnName = "conversation_id")]
        public string ConversationId { get; set; } = "";

        /// <summary>
        /// 压缩块主题
        /// </summary>
        [SugarColumn(ColumnName = "block_topic", IsNullable = true)]
        public string? BlockTopic { get; set; }

        /// <summary>
        /// 压缩块摘要
        /// </summary>
        [SugarColumn(ColumnName = "block_summary", IsNullable = true)]
        public string? BlockSummary { get; set; }

        /// <summary>
        /// 压缩块关键点（JSON数组）
        /// </summary>
        [SugarColumn(ColumnName = "block_key_points", IsNullable = true, IsJson = true)]
        public List<string>? BlockKeyPoints { get; set; }

        /// <summary>
        /// 压缩块提示
        /// </summary>
        [SugarColumn(ColumnName = "block_hint", IsNullable = true)]
        public string? BlockHint { get; set; }

        /// <summary>
        /// 压缩块起始消息ID
        /// </summary>
        [SugarColumn(ColumnName = "block_start_msg_id", IsNullable = true)]
        public int? BlockStartMsgId { get; set; }

        /// <summary>
        /// 压缩块结束消息ID
        /// </summary>
        [SugarColumn(ColumnName = "block_end_msg_id", IsNullable = true)]
        public int? BlockEndMsgId { get; set; }

        /// <summary>
        /// 压缩块压缩时间
        /// </summary>
        [SugarColumn(ColumnName = "block_compressed_at", IsNullable = true)]
        public DateTime? BlockCompressedAt { get; set; }

        /// <summary>
        /// 压缩块覆盖的消息数量
        /// </summary>
        [SugarColumn(ColumnName = "compressed_message_count")]
        public int CompressedMessageCount { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [SugarColumn(ColumnName = "created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 更新时间
        /// </summary>
        [SugarColumn(ColumnName = "updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
