using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.Plugins.Abstration;
using SqlSugar;
using System.ComponentModel;
using System.Text;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "使用自然语言查询关系型数据库的插件", Version = "v1.1")]
    public class Text2SQLPlugin : BasePlugin
    {
        private IServiceProvider _serviceProvider;
        private ConnectionConfig _connectionConfig;
        private PromptTemplateService _promptTemplateService;
        private ILogger<Text2SQLPlugin> _logger;

        [PluginParameter(Description = "连接字符串，目前仅支持 MySQL 数据库", Required = true)]
        public string ConnectionString { get; set; }

        [PluginParameter(Description = "数据库名称", Required = true)]
        public string Database { get; set; }

        public Text2SQLPlugin(IServiceProvider serviceProvider) : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();

            var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
            _logger = loggerFactory.CreateLogger<Text2SQLPlugin>();
        }

        public override void Initialize(long appId)
        {
            base.Initialize(appId);
            _connectionConfig = new ConnectionConfig() { DbType = DbType.MySql, ConnectionString = ConnectionString, IsAutoCloseConnection = true };
        }

        [KernelFunction]
        [Description("根据用户的输入生成和执行 SQL 并返回 Markdown 形式的表格数据")]
        public async Task<string> QueryAsync([Description("用户输入")] string input, Kernel kernel)
        {
            var tableDescriptor = await GetTableDescriptorsAsync(Database);
            var databaseSchema = GeneratorDatabaseSchema(tableDescriptor);

            var promptTemplate = _promptTemplateService.LoadTemplate("Text2SQL.txt");
            promptTemplate.AddVariable("input", input);
            promptTemplate.AddVariable("schema", databaseSchema);

            var functionResult = await promptTemplate.InvokeAsync(kernel);
            var generatedSQL = functionResult.GetValue<string>().Replace("```sql", "").Replace("```", "");
            _logger.LogInformation("Generated SQL: {0}", generatedSQL);

            var queryResult = await ExecuteSQLAsync(generatedSQL);
            return queryResult;
        }

        private async Task<IEnumerable<TableDescriptor>> GetTableDescriptorsAsync(string databaseName)
        {
            var sqlText =
                @"SELECT t.TABLE_NAME,
                     t.TABLE_COMMENT,
                     c.COLUMN_NAME,
                     c.COLUMN_COMMENT,
                     c.DATA_TYPE,
                     c.IS_NULLABLE
                FROM INFORMATION_SCHEMA.TABLES t
                LEFT JOIN INFORMATION_SCHEMA.COLUMNS c
                    ON c.TABLE_NAME = t.TABLE_NAME
                WHERE t.TABLE_SCHEMA = '{0}'";

            using var sqlClient = new SqlSugarClient(_connectionConfig);
            var rows = await sqlClient.Ado.SqlQueryAsync<dynamic>(string.Format(sqlText, databaseName));
            if (rows.Count == 0) return Enumerable.Empty<TableDescriptor>();

            return rows.GroupBy(x => x.TABLE_NAME).Select(g =>
            {
                return new TableDescriptor()
                {
                    Name = g.ToList()[0].TABLE_NAME,
                    Description = g.ToList()[0].TABLE_COMMENT,
                    Columns = AsColumnDescriptors(g.ToList())
                };
            }).ToList();
        }

        private IEnumerable<ColumnDescriptor> AsColumnDescriptors(List<dynamic> rows)
        {
            return rows.Select(x => new ColumnDescriptor()
            {
                Name = x.COLUMN_NAME,
                DataType = x.DATA_TYPE,
                Description = x.COLUMN_COMMENT,
                IsNullable = x.IS_NULLABLE == "YES"
            });
        }

        private string GeneratorDatabaseSchema(IEnumerable<TableDescriptor> tableDescriptors)
        {
            var stringBuilder = new StringBuilder();
            foreach (var tableDescriptor in tableDescriptors)
            {
                stringBuilder.AppendLine($"{tableDescriptor.Name}, {tableDescriptor.Description}");
                foreach (var columnDescriptor in tableDescriptor.Columns)
                {
                    stringBuilder.AppendLine($" - {columnDescriptor.Name}, {columnDescriptor.DataType}, {columnDescriptor.Description}");
                }

                stringBuilder.AppendLine();
            }

            return stringBuilder.ToString();
        }

        private async Task<string> ExecuteSQLAsync(string sql)
        {
            using var sqlClient = new SqlSugarClient(_connectionConfig);
            var rows = await sqlClient.Ado.SqlQueryAsync<dynamic>(sql);

            var columnNames = ((IDictionary<string, object>)rows[0]).Keys.ToList();

            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(RenderMarkdownTableHeader(columnNames));
            stringBuilder.AppendLine(RenderMarkdownTableBody(rows, columnNames));
            return stringBuilder.ToString();
        }

        private string RenderMarkdownTableHeader(List<string> columnNames)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(" | " + string.Join(" | ", columnNames) + " | ");

            var maxLength = columnNames.Max(x => x.Length);
            var headerDividers = columnNames.Select(x => new string('-', maxLength * 2));
            stringBuilder.AppendLine(" | " + string.Join(" | ", headerDividers) + " | ");
            return stringBuilder.ToString();
        }

        private string RenderMarkdownTableBody(List<dynamic> rows, List<string> columnNames)
        {
            var stringBuilder = new StringBuilder();

            foreach (var row in rows)
            {
                var rowValues = new List<string>();
                foreach (var columnName in columnNames)
                {
                    var value = ((IDictionary<string, object>)row)[columnName]?.ToString() ?? "NULL";
                    rowValues.Add(value);
                }
                stringBuilder.AppendLine(" | " + string.Join(" | ", rowValues) + " | ");
            }

            return stringBuilder.ToString();
        }

        internal record TableDescriptor
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public IEnumerable<ColumnDescriptor> Columns { get; set; }
        }

        internal record ColumnDescriptor
        {
            public string Name { get; set; }
            public string DataType { get; set; }
            public bool IsNullable { get; set; }
            public string Description { get; set; }
        }
    }
}
