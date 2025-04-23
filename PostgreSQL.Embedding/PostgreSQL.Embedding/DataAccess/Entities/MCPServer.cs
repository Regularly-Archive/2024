using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("mcp_servers")]
    public class MCPServer : BaseEntity
    {
        [SugarColumn(ColumnName = "name")]
        public string Name { get; set; }

        [SugarColumn(ColumnName = "transport_type")]
        public int TransportType { get; set; }

        [SugarColumn(ColumnName = "command")]
        public string Command { get; set; }

        [SugarColumn(ColumnName = "argumen")]
        public List<string> Arguments { get; set; }

        public Dictionary<string, string> EnvironmentVariables { get; set; }

        [SugarColumn(ColumnName = "app_id")]
        public long AppId { get; set; }
    }
}
