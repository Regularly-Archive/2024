using Microsoft.SemanticKernel;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "将自然语言转换为 MongoDB 查询语句（JavaScript 语法）。根据集合名称和样本文档生成查询脚本。", Version = "1.1")]
    public class Text2MongoDBPlugin : BasePlugin
    {
        /// <summary>
        /// 链接字符串
        /// </summary>
        [PluginParameter(Description = "MongoDB 连接字符串，如：mongodb://localhost:27017")] string ConnectionString { get; set; }

        /// <summary>
        /// 提示词模板
        /// </summary>
        private const string GENERATE_SCRIPT_PROMPT =
            """
            [Role]
            1. You are an agent designed to interact with a MongoDB database.
            2. Given an input question and collection name, create a syntactically correct MongoDB script to run.

            [Rules]
            1. You can query for all the documents by default unless the user specifies a specific number of examples they wish to obtain.
            2. You can order the results by a relevant column to return the most interesting examples in the database.
            3. You MUST query for all the fields from a specific collection unless user specifies related fields.
            4. You MUST double check your query before executing it. If you get an error while executing a query, rewrite the query and try again.
            5. You DO NOT make any DML statements (e.g., db.dropDatabase(),  db.collection.drop()...) to the database.
            6. You DO NOT need to explain to me the specific meaning of the MongoDB script.
            7. You DO NOT need to return any content other than the MongoDB script.
            8. You are only allowed to return one MongoDB script at a time.
            9. You must put the MongoDB script in a code block such as:
            ```js 

            ```

            You have access to the following collections: 

            {{$collectionNames}}

            This is a sample for the collection '{{$collectionName}}':
            
            ```json
            {{$schema}}
            ```

            At present, my inquiry is: {{$input}}
            """;

        /// <summary>
        /// IMongoDatabase
        /// </summary>
        private MongoDB.Driver.IMongoDatabase _database;

        public Text2MongoDBPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {

        }

        public override void Initialize(long appId)
        {
            base.Initialize(appId);

            if (string.IsNullOrEmpty(ConnectionString)) return;

            var mongoUrl = new MongoUrl(ConnectionString);
            var mongoClient = new MongoClient(mongoUrl);
            _database = mongoClient.GetDatabase(mongoUrl.DatabaseName);
        }

        [KernelFunction]
        [Description("根据用户描述生成 MongoDB 查询脚本（JavaScript 语法）。返回的脚本包含在代码块中，可直接复制执行。")]
        public async Task<string> GenerateScriptAsync(
            [Description("要查询的 MongoDB 集合名称")] string collectionName,
            [Description("用户想要查询的内容描述")] string query,
            Kernel kernel
        )
        {
            var collectionNames = string.Join("\r\n", GetCollectionNames().Select(x => $"- {x}"));
            var exampleJson = GetExampleDocument(collectionName);

            var clonedKernel = kernel.Clone();

            var promptTemplate = new CallablePromptTemplate(GENERATE_SCRIPT_PROMPT);
            promptTemplate.AddVariable("collectionNames", collectionNames);
            promptTemplate.AddVariable("collectionName", collectionName);
            promptTemplate.AddVariable("schema", exampleJson);
            promptTemplate.AddVariable("input", query);

            var functionResult = await promptTemplate.InvokeAsync(clonedKernel);
            return functionResult.GetValue<string>()?.Replace("```sql", "```js");
        }

        /// <summary>
        /// 获取集合名称列表
        /// </summary>
        /// <returns></returns>
        private IEnumerable<string> GetCollectionNames() => _database.ListCollectionNames().ToList();

        /// <summary>
        /// 获取示例文档
        /// </summary>
        /// <param name="collectionName"></param>
        /// <returns></returns>
        private string GetExampleDocument(string collectionName)
        {
            var collection = _database.GetCollection<BsonDocument>(collectionName);
            var document = collection.Find(_ => true).FirstOrDefault();
            return document == null ? JsonConvert.SerializeObject(new { }) : document.ToJson();
        }
    }
}
