using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.LlmServices;
using SqlSugar;
using System.ComponentModel;
using System.Text;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "使用自然语言查询数据库的插件")]
    public class Text2SQLPlugin
    {
        private IServiceProvider _serviceProvider;
        private ConnectionConfig _connectionConfig;
        private PromptTemplateService _promptTemplateService;
        private ILogger<Text2SQLPlugin> _logger;

        public Text2SQLPlugin(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();

            var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
            _logger = loggerFactory.CreateLogger<Text2SQLPlugin>();

            _connectionConfig = new ConnectionConfig()
            {

                DbType = DbType.MySql,
                ConnectionString = "Server=localhost;Database=Chinook;Uid=root;Pwd=1qaz2wsx3edc;Charset='utf8';SslMode=None;",
                IsAutoCloseConnection = true,
            };
        }

        [KernelFunction]
        [Description("根据用户的输入生成和执行 SQL 并返回 Markdown 形式的表格数据")]
        public async Task<string> QueryAsync([Description("用户输入")] string input, Kernel kernel)
        {
            var tableDescriptor = await GetTableTableDescriptorsAsync("Chinook");
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

        private async Task<IEnumerable<TableDescriptor>> GetTableTableDescriptorsAsync(string databaseName)
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
                foreach(var columnDescriptor in tableDescriptor.Columns)
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

            var columns = ((IDictionary<string, object>)rows[0]).Keys.ToList();

            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(RenderMarkdownTableHeader(columns));
            stringBuilder.AppendLine(RenderMarkdownTableBody(rows, columns));
            return stringBuilder.ToString();
        }

        private string RenderMarkdownTableHeader(List<string> columns)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(" | " + string.Join(" | ", columns) + " | ");

            var maxLength = columns.Max(x => x.Length);
            var headerDividers = columns.Select(x => new string('-', maxLength * 2));
            stringBuilder.AppendLine(" | " + string.Join(" | ", headerDividers) + " | ");
            return stringBuilder.ToString();
        }

        private string RenderMarkdownTableBody(List<dynamic> rows, List<string> columns)
        {
            var stringBuilder = new StringBuilder();

            foreach (var row in rows)
            {
                var rowValues = new List<string>();
                foreach (var column in columns)
                {

                    var value = ((IDictionary<string, object>)row)[column]?.ToString() ?? "NULL";
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
