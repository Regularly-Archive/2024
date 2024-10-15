using Microsoft.SemanticKernel;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "使用自然语言查询 MongoDB 的插件")]
    public class Text2MongoDBPlugin : BasePlugin
    {
        /// <summary>
        /// 链接字符串
        /// </summary>
        [PluginParameter(Description = "连接字符串", Required = true)]
        private string ConnectionString { get; set; }

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
        [Description("根据用户输入生成 MongoDB 脚本")]
        public async Task<string> GenerateScriptAsync([Description("集合名称")] string collectionName, [Description("用户输入")] string query, Kernel kernel)
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
