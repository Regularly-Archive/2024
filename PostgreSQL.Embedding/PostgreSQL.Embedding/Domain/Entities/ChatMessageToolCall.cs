using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities
{
    /// <summary>
    /// 工具调用
    /// </summary>
    [SugarTable("chat_message_tool_calls")]
    public class ChatMessageToolCall : BaseEntity
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
        /// 工具名称
        /// </summary>
        [SugarColumn(ColumnName = "name")]
        public string Name { get; set; } = "";

        /// <summary>
        /// 输入参数（JSON）
        /// </summary>
        [SugarColumn(ColumnName = "input", IsJson = true)]
        public Dictionary<string, object>? Input { get; set; }

        /// <summary>
        /// 输出结果
        /// </summary>
        [SugarColumn(ColumnName = "output", ColumnDataType = "text", IsNullable = true)]
        public string? Output { get; set; }

        /// <summary>
        /// 状态（0=pending, 1=success, 2=error）
        /// </summary>
        [SugarColumn(ColumnName = "status")]
        public int Status { get; set; }

        /// <summary>
        /// 持续时长（毫秒）
        /// </summary>
        [SugarColumn(ColumnName = "duration_ms", IsNullable = true)]
        public long? DurationMs { get; set; }
    }
}
