using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities;

[SugarTable("agent_runs")]
public class AgentRun : BaseEntity
{
    [SugarColumn(ColumnName = "run_id")]
    public string RunId {  get; set; }

    [SugarColumn(ColumnName = "conversation_id")]
    public string ConversationId  { get; set; }

    [SugarColumn(ColumnName = "ref_message_id")]
    public long RefMessageId { get; set; }

    [SugarColumn(ColumnName = "message_id")]
    public long MessageId {  get; set; }
}
