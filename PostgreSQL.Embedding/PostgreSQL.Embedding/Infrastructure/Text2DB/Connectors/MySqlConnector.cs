using System.Diagnostics;
using SqlSugar;

namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// MySQL 数据源连接器
/// </summary>
public class MySqlConnector : IDataSourceConnector
{
    private SqlSugarClient? _client;

    public DataSourceType DataSourceType => DataSourceType.MySQL;
    public bool CanExecute => true;

    public Task ConnectAsync(string connectionString)
    {
        _client = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.MySql,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true
        });
        return Task.CompletedTask;
    }

    public async Task<DatabaseSchema> GetSchemaAsync()
    {
        var schema = new DatabaseSchema { Type = DataSourceType };

        // 获取所有表
        var tables = await _client!.Ado.SqlQueryAsync<dynamic>(@"
            SELECT TABLE_NAME, TABLE_COMMENT
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()");

        foreach (var table in tables)
        {
            var tableName = (string)table.TABLE_NAME;

            // 获取列信息
            var columns = await _client.Ado.SqlQueryAsync<dynamic>(@"
                SELECT COLUMN_NAME, DATA_TYPE, COLUMN_COMMENT, IS_NULLABLE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @TableName
                ORDER BY ORDINAL_POSITION",
                new { TableName = tableName });

            var tableInfo = new TableInfo
            {
                Name = tableName,
                Description = (string)(table.TABLE_COMMENT ?? ""),
                Columns = columns.Select(c => new ColumnInfo
                {
                    Name = (string)c.COLUMN_NAME,
                    DataType = (string)c.DATA_TYPE,
                    Description = (string)(c.COLUMN_COMMENT ?? ""),
                    IsNullable = c.IS_NULLABLE?.ToString() == "YES"
                }).ToList()
            };

            schema.Tables.Add(tableInfo);
        }

        return schema;
    }

    public async Task<List<string>> GetTableNamesAsync()
    {
        var tables = await _client!.Ado.SqlQueryAsync<dynamic>(@"
            SELECT TABLE_NAME
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'");

        return tables.Select(t => (string)t.TABLE_NAME).ToList();
    }

    public async Task<List<Dictionary<string, object?>>> GetSampleDataAsync(string tableName, int limit = 3)
    {
        var sql = $"SELECT * FROM `{tableName}` LIMIT {limit}";
        var rows = await _client!.Ado.SqlQueryAsync<dynamic>(sql);

        return rows.Select(row =>
        {
            var dict = new Dictionary<string, object?>();
            var rowDict = (IDictionary<string, object>)row;
            foreach (var kvp in rowDict)
            {
                dict[kvp.Key] = kvp.Value;
            }
            return dict;
        }).ToList();
    }

    public async Task<QueryResult> ExecuteQueryAsync(string query)
    {
        var sw = Stopwatch.StartNew();
        var rows = await _client!.Ado.SqlQueryAsync<dynamic>(query);
        sw.Stop();

        return new QueryResult
        {
            QueryType = GetQueryType(query),
            Data = rows,
            ExecutionTimeMs = sw.ElapsedMilliseconds,
            RowCount = rows.Count
        };
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await _client!.Ado.GetScalarAsync("SELECT 1");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static QueryType GetQueryType(string query)
    {
        var trimmed = query.Trim().ToUpperInvariant();
        if (trimmed.StartsWith("SELECT")) return QueryType.Select;
        if (trimmed.StartsWith("INSERT")) return QueryType.Insert;
        if (trimmed.StartsWith("UPDATE")) return QueryType.Update;
        if (trimmed.StartsWith("DELETE")) return QueryType.Delete;
        if (trimmed.StartsWith("DROP")) return QueryType.Drop;
        return QueryType.Other;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
