using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("mcp_tools")]
    public class MCPTool : BaseEntity
    {
        [SugarColumn(ColumnName = "server_name")]
        public string ServerName { get; set; }

        [SugarColumn(ColumnName = "server_version")]
        public string ServerVersion { get; set; }

        [SugarColumn(ColumnName = "tool_name")]
        public string ToolName { get; set; }

        [SugarColumn(ColumnName = "tool_description")]
        public string ToolDescription { get; set; }

        [SugarColumn(ColumnName = "tool_input_schema", ColumnDataType = "text")]
        public string ToolInputSchema { get; set; }
    }
}
