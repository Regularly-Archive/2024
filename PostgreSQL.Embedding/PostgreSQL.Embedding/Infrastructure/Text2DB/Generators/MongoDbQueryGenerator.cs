using System.Text;
using System.Text.Json;

namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// MongoDB 查询生成器
/// </summary>
public class MongoDbQueryGenerator : IQueryGenerator
{
    public DataSourceType DataSourceType => DataSourceType.MongoDB;

    public Task<string> GenerateAsync(DatabaseSchema schema, string userQuestion)
    {
        // 构建 Schema 描述
        var schemaText = BuildSchemaDescription(schema);

        var prompt = $"""
[Role]
1. You are an agent designed to interact with a MongoDB database.
2. Given an input question and collection name, create a syntactically correct MongoDB script to run.

[Rules]
1. You can query for all the documents by default unless the user specifies a specific number of examples they wish to obtain.
2. You can order the results by a relevant column to return the most interesting examples in the database.
3. You MUST query for all the fields from a specific collection unless user specifies related fields.
4. You MUST double check your query before executing it. If you get an error while executing a query, rewrite the query and try again.
5. You DO NOT make any DML statements (e.g., db.dropDatabase(), db.collection.drop()...) to the database.
6. You DO NOT need to explain to me the specific meaning of the MongoDB script.
7. You DO NOT need to return any content other than the MongoDB script.
8. You are only allowed to return one MongoDB script at a time.
9. You must put the MongoDB script in a code block such as:
```js

```

You have access to the following collections:

{schemaText}

At present, my inquiry is: {userQuestion}
""";

        return Task.FromResult(prompt);
    }

    public Task<string> FormatResultAsync(QueryResult result)
    {
        var sb = new StringBuilder();

        if (result.Data is IEnumerable<dynamic> rows)
        {
            var rowList = rows.ToList();
            if (rowList.Count == 0)
            {
                return Task.FromResult("查询结果为空");
            }

            // 格式化为 JSON
            var json = JsonSerializer.Serialize(rowList, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            sb.AppendLine("```json");
            sb.AppendLine(json);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine($"共 {result.RowCount} 行，耗时 {result.ExecutionTimeMs}ms");
        }

        return Task.FromResult(sb.ToString());
    }

    private static string BuildSchemaDescription(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"- {table.Name}");

            // 添加样例文档
            if (table.SampleData.Count > 0)
            {
                sb.AppendLine($"  样例文档:");
                foreach (var sample in table.SampleData.Take(2))
                {
                    var sampleJson = JsonSerializer.Serialize(sample);
                    sb.AppendLine($"  ```json");
                    sb.AppendLine($"  {sampleJson}");
                    sb.AppendLine($"  ```");
                }
            }
        }

        return sb.ToString();
    }
}
