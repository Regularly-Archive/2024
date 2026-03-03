using System.Diagnostics;
using SqlSugar;

namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// PostgreSQL 数据源连接器
/// </summary>
public class PostgreSqlConnector : IDataSourceConnector
{
    private SqlSugarClient? _client;

    public DataSourceType DataSourceType => DataSourceType.PostgreSQL;
    public bool CanExecute => true;

    public Task ConnectAsync(string connectionString)
    {
        _client = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.PostgreSQL,
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
            SELECT table_name, obj_description((table_schema || '.' || table_name)::regclass) as table_comment
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'");

        foreach (var table in tables)
        {
            var tableName = (string)table.table_name;

            // 获取列信息
            var columns = await _client.Ado.SqlQueryAsync<dynamic>(@"
                SELECT
                    c.column_name,
                    c.data_type,
                    col_description((c.table_schema || '.' || c.table_name)::regclass, c.ordinal_position) as column_description,
                    c.is_nullable
                FROM information_schema.columns c
                WHERE c.table_schema = 'public' AND c.table_name = @TableName
                ORDER BY c.ordinal_position",
                new { TableName = tableName });

            var tableInfo = new TableInfo
            {
                Name = tableName,
                Description = (string)(table.table_comment ?? ""),
                Columns = columns.Select(c => new ColumnInfo
                {
                    Name = (string)c.column_name,
                    DataType = (string)c.data_type,
                    Description = (string)(c.column_description ?? ""),
                    IsNullable = c.is_nullable?.ToString() == "YES"
                }).ToList()
            };

            schema.Tables.Add(tableInfo);
        }

        return schema;
    }

    public async Task<List<string>> GetTableNamesAsync()
    {
        var tables = await _client!.Ado.SqlQueryAsync<dynamic>(@"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'");

        return tables.Select(t => (string)t.table_name).ToList();
    }

    public async Task<List<Dictionary<string, object?>>> GetSampleDataAsync(string tableName, int limit = 3)
    {
        var sql = $"SELECT * FROM \"{tableName}\" LIMIT {limit}";
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
