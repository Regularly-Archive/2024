using PostgreSQL.Embedding.Domain.Models.Planners;
using SqlSugar;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Domain.Entities
{
    /// <summary>
    /// 执行计划
    /// </summary>
    [SugarTable("chat_message_plans")]
    public class ChatMessagePlan : BaseEntity
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
        /// 计划ID（唯一标识，用于更新）
        /// </summary>
        [SugarColumn(ColumnName = "plan_id")]
        public long PlanId { get; set; }

        /// <summary>
        /// 计划/子任务标题
        /// </summary>
        [SugarColumn(ColumnName = "title", ColumnDataType = "text")]
        public string Title { get; set; } = "";

        /// <summary>
        /// 计划/子任务描述
        /// </summary>
        [SugarColumn(ColumnName = "description", ColumnDataType = "text")]
        public string Description { get; set; }

        /// <summary>
        /// 计划/子任务输出
        /// </summary>
        [SugarColumn(ColumnName = "output", ColumnDataType = "text")]
        public string Output { get; set; } = "";

        /// <summary>
        /// 计划/子任务状态
        /// </summary>
        [SugarColumn(ColumnName = "status")]
        public int Status { get; set; }

    }
}
