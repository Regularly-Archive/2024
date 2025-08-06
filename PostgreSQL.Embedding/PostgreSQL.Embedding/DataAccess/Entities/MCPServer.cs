using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("mcp_servers")]
    public class MCPServer : BaseEntity
    {
        [SugarColumn(ColumnName = "name", IsNullable = true)]
        public string Name { get; set; }

        [SugarColumn(ColumnName = "intro", IsNullable = true, ColumnDataType = "text")]
        public string Intro { get; set; }

        [SugarColumn(ColumnName = "transport_type", IsNullable = true)]
        public int TransportType { get; set; }

        [SugarColumn(ColumnName = "command")]
        public string Command { get; set; }

        [SugarColumn(ColumnName = "arguments", IsJson = true, IsNullable = true)]
        public string[] Arguments { get; set; }

        [SugarColumn(ColumnName = "env_vars", IsJson = true, IsNullable = true)]
        public Dictionary<string, string> EnvVars { get; set; }

        [SugarColumn(ColumnName = "endpoint", IsNullable = true)]
        public string Endpoint { get; set; }

        [SugarColumn(ColumnName = "extra_headers", IsJson = true, IsNullable = true)]
        public Dictionary<string, string> ExtraHeaders { get; set; }

        [SugarColumn(ColumnName = "app_id")]
        public long AppId { get; set; }

        [SugarColumn(ColumnName = "anabled", DefaultValue = "TRUE")]
        public bool Enabled { get; set; }
    }
}
