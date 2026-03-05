using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// MongoDB 数据源连接器（只生成脚本，不执行）
/// </summary>
public class MongoDbConnector : IDataSourceConnector
{
    private MongoClient? _client;
    private IMongoDatabase? _database;

    public DataSourceType DataSourceType => DataSourceType.MongoDB;

    /// <summary>MongoDB 支持执行 JavaScript 脚本</summary>
    public bool CanExecute => true;

    public Task ConnectAsync(string connectionString)
    {
        var mongoUrl = new MongoUrl(connectionString);
        _client = new MongoClient(mongoUrl);
        _database = _client.GetDatabase(mongoUrl.DatabaseName);
        return Task.CompletedTask;
    }

    public async Task<DatabaseSchema> GetSchemaAsync()
    {
        var schema = new DatabaseSchema { Type = DataSourceType };

        // 获取所有集合名称
        var collectionNames = await _database!.ListCollectionNamesAsync();

        foreach (var collectionName in collectionNames.ToList())
        {
            var collection = _database.GetCollection<BsonDocument>(collectionName);

            // 取 3 条样例文档
            var sampleDocs = await collection.Find(_ => true).Limit(3).ToListAsync();

            var tableInfo = new TableInfo
            {
                Name = collectionName,
                Description = "",
                SampleData = sampleDocs.Select(doc => ConvertBsonToDict(doc)).ToList()
            };

            schema.Tables.Add(tableInfo);
        }

        return schema;
    }

    public async Task<List<string>> GetTableNamesAsync()
    {
        var collectionNames = await _database!.ListCollectionNamesAsync();
        return collectionNames.ToList();
    }

    public async Task<List<Dictionary<string, object?>>> GetSampleDataAsync(string collectionName, int limit = 3)
    {
        var collection = _database!.GetCollection<BsonDocument>(collectionName);
        var sampleDocs = await collection.Find(_ => true).Limit(limit).ToListAsync();
        return sampleDocs.Select(doc => ConvertBsonToDict(doc)).ToList();
    }

    public async Task<QueryResult> ExecuteQueryAsync(string query)
    {
        // 解析脚本，提取集合名和 pipeline
        var parsed = ParseMongoScript(query);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var collection = _database!.GetCollection<BsonDocument>(parsed.CollectionName);

        // 将 JSON 数组字符串转换为 BsonDocument 数组
        var pipelineStages = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonArray>(parsed.Pipeline);
        var pipeline = pipelineStages.Cast<BsonDocument>().ToArray();

        var results = await collection.Aggregate<BsonDocument>(pipeline).ToListAsync();

        sw.Stop();

        return new QueryResult
        {
            QueryType = QueryType.Select,
            Data = results.Select(r => ConvertBsonToDict(r)).ToList(),
            ExecutionTimeMs = sw.ElapsedMilliseconds,
            RowCount = results.Count
        };
    }

    private static (string CollectionName, string Pipeline) ParseMongoScript(string script)
    {
        var collectionName = "";
        var pipeline = "";

        script = script.Trim();
        if (script.StartsWith("{"))
        {
            var jsonDoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(script);

            if (jsonDoc.Contains("collection"))
            {
                collectionName = jsonDoc["collection"].AsString;
            }

            if (jsonDoc.Contains("pipeline"))
            {
                pipeline = jsonDoc["pipeline"].ToJson();
            }
        }

        return (collectionName, pipeline);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await _database!.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, object?> ConvertBsonToDict(BsonDocument doc)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var element in doc.Elements)
        {
            dict[element.Name] = ConvertBsonValue(element.Value);
        }
        return dict;
    }

    private static object? ConvertBsonValue(BsonValue value)
    {
        if (value.BsonType == BsonType.Null) return null;
        if (value.IsObjectId) return value.AsObjectId.ToString();
        if (value.IsValidDateTime) return value.ToUniversalTime();
        if (value.IsInt32) return value.AsInt32;
        if (value.IsInt64) return value.AsInt64;
        if (value.IsDouble) return value.AsDouble;
        if (value.IsBoolean) return value.AsBoolean;
        if (value.IsString) return value.AsString;
        if (value.BsonType == BsonType.Array) return value.AsBsonArray.Select(ConvertBsonValue).ToList();
        if (value.BsonType == BsonType.Document) return ConvertBsonToDict(value.AsBsonDocument);
        return value.ToString();
    }

    public void Dispose()
    {
        _client = null;
        _database = null;
    }
}
