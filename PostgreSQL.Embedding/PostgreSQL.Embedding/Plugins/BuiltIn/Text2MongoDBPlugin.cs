using Microsoft.SemanticKernel;
using MongoDB.Driver;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Infrastructure.Text2DB;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "将自然语言转换为 MongoDB 查询语句（JavaScript 语法）。根据集合名称和样本文档生成查询脚本，支持执行查询。", Version = "2.0")]
    public class Text2MongoDBPlugin : BasePlugin
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Text2DBService _text2DbService;
        private readonly PromptTemplateService _promptTemplateService;

        public Text2MongoDBPlugin(
            IServiceProvider serviceProvider,
            Text2DBService text2DbService,
            PromptTemplateService promptTemplateService) : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _text2DbService = text2DbService;
            _promptTemplateService = promptTemplateService;
        }

        [KernelFunction]
        [Description("查询当前应用下所有可用的 MongoDB 数据源")]
        public async Task<List<DataSourceDto>> ListDataSourcesAsync([Description("所属应用 ID")] long appId)
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<DataSource>>();

            var dataSources = await repo.FindListAsync(x =>
                x.AppId == appId &&
                x.IsEnabled == true &&
                x.Type == DataSourceType.MongoDB);

            return dataSources.Select(ds => new DataSourceDto
            {
                Id = ds.Id,
                Name = ds.Name,
                Type = ds.Type,
                TypeName = "MongoDB",
                Description = ds.Description,
                AppId = ds.AppId,
                IsEnabled = ds.IsEnabled
            }).ToList();
        }

        [KernelFunction]
        [Description("列出数据源中的所有集合名称")]
        public async Task<List<string>> ListCollectionsAsync([Description("数据源 ID")] long dataSourceId)
        {
            IDataSourceConnector? connector = null;
            try
            {
                connector = await GetConnectorAsync(dataSourceId);
                return await connector.GetTableNamesAsync();
            }
            finally
            {
                connector?.Dispose();
            }
        }

        [KernelFunction]
        [Description("获取指定集合的样例数据，用于了解数据结构")]
        public async Task<List<Dictionary<string, object?>>> GetDataSampleAsync(
            [Description("数据源 ID")] long dataSourceId,
            [Description("集合名称")] string collectionName,
            [Description("返回条数，默认 3")] int limit = 3)
        {
            IDataSourceConnector? connector = null;
            try
            {
                connector = await GetConnectorAsync(dataSourceId);
                return await connector.GetSampleDataAsync(collectionName, limit);
            }
            finally
            {
                connector?.Dispose();
            }
        }

        [KernelFunction]
        [Description("根据用户描述生成 MongoDB 查询脚本（JavaScript 语法）。返回的脚本包含在代码块中，可直接复制执行。")]
        public async Task<string> GenerateScriptAsync(
            [Description("数据源 ID")] long dataSourceId,
            [Description("要查询的 MongoDB 集合名称")] string collectionName,
            [Description("用户想要查询的内容描述")] string query,
            Kernel kernel)
        {
            IDataSourceConnector? connector = null;
            try
            {
                connector = await GetConnectorAsync(dataSourceId);

                var schema = await connector.GetSchemaAsync();
                var collection = schema.Tables.FirstOrDefault(t => t.Name == collectionName);

                if (collection == null)
                    throw new ArgumentException($"The collection '{collectionName}' not found");

                // 构建集合列表和样例文档
                var collectionNames = string.Join("\r\n", schema.Tables.Select(x => $"- {x.Name}"));
                var sampleJson = collection.SampleData.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(collection.SampleData[0])
                    : "{}";

                var clonedKernel = kernel.Clone();

                var promptTemplate = _promptTemplateService.LoadTemplate("Text2MongoDB.txt");
                promptTemplate.AddVariable("collectionNames", collectionNames);
                promptTemplate.AddVariable("collectionName", collectionName);
                promptTemplate.AddVariable("schema", sampleJson);
                promptTemplate.AddVariable("input", query);

                var functionResult = await promptTemplate.InvokeAsync(clonedKernel);
                return functionResult.GetValue<string>()?.Replace("```json", "").Replace("```", "") ?? "";
            }
            finally
            {
                connector?.Dispose();
            }
        }

        [KernelFunction]
        [Description("根据用户描述生成并执行 MongoDB 查询，返回 JSON 格式的查询结果。")]
        public async Task<string> QueryAsync(
            [Description("数据源 ID")] long dataSourceId,
            [Description("用户想要查询的内容描述")] string input,
            Kernel kernel)
        {
            IDataSourceConnector? connector = null;
            try
            {
                connector = await GetConnectorAsync(dataSourceId);

                // 获取 Schema
                var schema = await connector.GetSchemaAsync();

                // 构建 Schema 描述
                var schemaText = BuildSchemaDescription(schema);

                var clonedKernel = kernel.Clone();

                var promptTemplate = _promptTemplateService.LoadTemplate("Text2MongoDB.txt");
                promptTemplate.AddVariable("collectionNames", schemaText);
                promptTemplate.AddVariable("collectionName", "");  // 让 LLM 选择合适的集合
                promptTemplate.AddVariable("schema", "{}");
                promptTemplate.AddVariable("input", input);

                var functionResult = await promptTemplate.InvokeAsync(clonedKernel);
                var generatedScript = functionResult.GetValue<string>()?.Replace("```json", "").Replace("```", "") ?? "";

                // 执行查询
                var queryResult = await connector.ExecuteQueryAsync(generatedScript);

                // 格式化结果
                var generator = _text2DbService.CreateQueryGenerator(DataSourceType.MongoDB);
                var formattedResult = await generator.FormatResultAsync(queryResult);

                return JsonConvert.SerializeObject(new
                {
                    script = generatedScript,
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

        private async Task<IDataSourceConnector> GetConnectorAsync(long dataSourceId)
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<DataSource>>();

            var dataSource = await repo.FindAsync(x => x.Id == dataSourceId);
            if (dataSource == null)
            {
                throw new ArgumentException($"DataSource with ID {dataSourceId} not found");
            }

            if (dataSource.Type != DataSourceType.MongoDB)
            {
                throw new ArgumentException($"DataSource type {dataSource.Type} is not MongoDB");
            }

            var connector = _text2DbService.CreateConnector(DataSourceType.MongoDB);
            await connector.ConnectAsync(dataSource.ConnectionString);

            return connector;
        }

        private static string BuildSchemaDescription(DatabaseSchema schema)
        {
            var sb = new System.Text.StringBuilder();

            foreach (var table in schema.Tables)
            {
                sb.AppendLine($"- {table.Name}");

                if (table.SampleData.Count > 0)
                {
                    sb.AppendLine("  样例文档:");
                    foreach (var sample in table.SampleData.Take(2))
                    {
                        sb.AppendLine($"  ```json");
                        sb.AppendLine($"  {System.Text.Json.JsonSerializer.Serialize(sample)}");
                        sb.AppendLine($"  ```");
                    }
                }
            }

            return sb.ToString();
        }
    }
}
