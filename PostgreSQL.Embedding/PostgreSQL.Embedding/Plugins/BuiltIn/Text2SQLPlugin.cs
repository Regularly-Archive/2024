using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Infrastructure.Text2DB;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Plugins.Abstration;
using SqlSugar;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "将自然语言转换为 SQL 查询语句并在关系型数据库（MySQL/PostgreSQL/SQLServer等）中执行，返回 Markdown 表格格式的查询结果", Version = "2.0")]
    public class Text2SQLPlugin : BasePlugin
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Text2DBService _text2DbService;
        private readonly PromptTemplateService _promptTemplateService;
        private readonly ILogger<Text2SQLPlugin> _logger;

        /// <summary>
        /// 支持的关系型数据库类型
        /// </summary>
        private static readonly DataSourceType[] SupportedTypes =
        {
            DataSourceType.MySQL,
            DataSourceType.PostgreSQL,
            DataSourceType.SQLServer,
            DataSourceType.Oracle,
            DataSourceType.SQLite,
            DataSourceType.DuckDB
        };

        public Text2SQLPlugin(
            IServiceProvider serviceProvider,
            Text2DBService text2DbService,
            PromptTemplateService promptTemplateService) : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _text2DbService = text2DbService;
            _promptTemplateService = promptTemplateService;

            var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
            _logger = loggerFactory.CreateLogger<Text2SQLPlugin>();
        }

        /// <summary>
        /// 根据 DataSource 获取连接配置
        /// </summary>
        private async Task<(DataSource DataSource, IDataSourceConnector Connector)> GetConnectorAsync(long dataSourceId)
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<DataSource>>();

            var dataSource = await repo.FindAsync(x => x.Id == dataSourceId);
            if (dataSource == null)
            {
                throw new ArgumentException($"DataSource with ID {dataSourceId} not found");
            }

            if (!SupportedTypes.Contains(dataSource.Type))
            {
                throw new ArgumentException($"DataSource type {dataSource.Type} is not supported by Text2SQLPlugin");
            }

            // 通过 Text2DBService 创建对应类型的 Connector
            var connector = _text2DbService.CreateConnector(dataSource.Type);
            await connector.ConnectAsync(dataSource.ConnectionString);

            return (dataSource, connector);
        }

        [KernelFunction]
        [Description("查询当前应用下所有可用的关系型数据库（MySQL/PostgreSQL/SQLServer/Oracle/SQLite/DuckDB）数据源")]
        public async Task<List<DataSourceDto>> ListDataSourcesAsync([Description("所属应用 ID")] long appId)
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<DataSource>>();

            var dataSources = await repo.FindListAsync(x =>
                x.AppId == appId &&
                x.IsEnabled == true &&
                x.Type != DataSourceType.MongoDB
            );

            return dataSources.Select(ds => new DataSourceDto
            {
                Id = ds.Id,
                Name = ds.Name,
                Type = ds.Type,
                TypeName = GetTypeName(ds.Type),
                Description = ds.Description,
                AppId = ds.AppId,
                IsEnabled = ds.IsEnabled
            }).ToList();
        }

        [KernelFunction]
        [Description("根据用户描述生成并执行 SQL 查询，返回 Markdown 表格形式的查询结果。同时会通过 Artifacts 事件发送原始数据表。")]
        public async Task<string> QueryAsync(
            [Description("数据源 ID")] long dataSourceId,
            [Description("用户想要查询的内容描述")] string input,
            Kernel kernel)
        {
            IDataSourceConnector? connector = null;
            try
            {
                var (dataSource, conn) = await GetConnectorAsync(dataSourceId);
                connector = conn;

                // 通过 Connector 获取 Schema
                var schema = await connector.GetSchemaAsync();

                // 构建 Schema 描述
                var schemaText = BuildSchemaDescription(schema);

                var promptTemplate = _promptTemplateService.LoadTemplate("Text2SQL.txt");
                promptTemplate.AddVariable("input", input);
                promptTemplate.AddVariable("schema", schemaText);
                promptTemplate.AddVariable("dbType", dataSource.Type.ToString());

                var functionResult = await promptTemplate.InvokeAsync(kernel);
                var generatedSQL = functionResult.GetValue<string>().Replace("```sql", "").Replace("```", "");
                _logger.LogInformation("Generated SQL: {0}", generatedSQL);

                // 通过 Connector 执行查询
                var queryResult = await connector.ExecuteQueryAsync(generatedSQL);

                // 通过 Generator 格式化结果
                var generator = _text2DbService.CreateQueryGenerator(dataSource.Type);

                return JsonConvert.SerializeObject(new
                {
                    sql = generatedSQL,
                    result = JsonConvert.SerializeObject(queryResult.Data),
                    rowCount = queryResult.RowCount,
                    executionTimeMs = queryResult.ExecutionTimeMs
                });
            }
            finally
            {
                connector?.Dispose();
            }
        }

        private static string GetTypeName(DataSourceType type)
        {
            return type switch
            {
                DataSourceType.MySQL => "MySQL",
                DataSourceType.PostgreSQL => "PostgreSQL",
                DataSourceType.SQLServer => "SQL Server",
                DataSourceType.Oracle => "Oracle",
                DataSourceType.SQLite => "SQLite",
                DataSourceType.DuckDB => "DuckDB",
                DataSourceType.MongoDB => "MongoDB",
                DataSourceType.Excel => "Excel",
                DataSourceType.CSV => "CSV",
                DataSourceType.JSON => "JSON",
                _ => type.ToString()
            };
        }

        private static string BuildSchemaDescription(DatabaseSchema schema)
        {
            var sb = new System.Text.StringBuilder();

            foreach (var table in schema.Tables)
            {
                sb.AppendLine($"{table.Name} - {table.Description}");

                foreach (var column in table.Columns)
                {
                    var nullable = column.IsNullable ? "NULL" : "NOT NULL";
                    sb.AppendLine($"  - {column.Name}: {column.DataType} ({nullable}) - {column.Description}");
                }

                // 添加样例数据（如果有）
                if (table.SampleData.Count > 0)
                {
                    sb.AppendLine("  样例数据:");
                    foreach (var sample in table.SampleData.Take(2))
                    {
                        var sampleJson = System.Text.Json.JsonSerializer.Serialize(sample);
                        sb.AppendLine($"    {sampleJson}");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
