using System.Text;
using System.Text.Json;

namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// SQL 查询生成器（适用于 MySQL、PostgreSQL、SQLServer 等关系型数据库）
/// </summary>
public class SqlQueryGenerator : IQueryGenerator
{
    public DataSourceType DataSourceType { get; }

    public SqlQueryGenerator(DataSourceType dataSourceType)
    {
        DataSourceType = dataSourceType;
    }

    public Task<string> GenerateAsync(DatabaseSchema schema, string userQuestion)
    {
        // 构建 Schema 描述
        var schemaText = BuildSchemaDescription(schema);

        var prompt = $"""
## 数据库信息
- 类型: {DataSourceType}
- 表结构:
{schemaText}

## 用户问题
{userQuestion}

## 要求
1. 根据表结构生成 SQL 查询
2. 只返回 SQL 语句，不要其他解释
3. 注意 {DataSourceType} 语法差异（如有）
""";

        // 这里应该调用 LLM 生成 SQL
        // 暂时返回 prompt，实际使用时由调用方传入 LLM
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

            // 获取列名
            var firstRow = (IDictionary<string, object>)rowList[0];
            var columns = firstRow.Keys.ToList();

            // 表头
            sb.AppendLine("| " + string.Join(" | ", columns) + " |");
            sb.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");

            // 数据行
            foreach (var row in rowList)
            {
                var rowDict = (IDictionary<string, object>)row;
                var values = columns.Select(c => FormatValue(rowDict[c]));
                sb.AppendLine("| " + string.Join(" | ", values) + " |");
            }

            sb.AppendLine();
            sb.AppendLine($"共 {result.RowCount} 行，耗时 {result.ExecutionTimeMs}ms");
        }
        else
        {
            sb.AppendLine(result.Data?.ToString() ?? "无结果");
        }

        return Task.FromResult(sb.ToString());
    }

    private static string BuildSchemaDescription(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

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
                    var sampleJson = JsonSerializer.Serialize(sample);
                    sb.AppendLine($"    {sampleJson}");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatValue(object? value)
    {
        if (value == null) return "NULL";
        if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss");
        if (value is bool b) return b ? "true" : "false";
        return value.ToString()?.Replace("|", "\\|") ?? "";
    }
}
