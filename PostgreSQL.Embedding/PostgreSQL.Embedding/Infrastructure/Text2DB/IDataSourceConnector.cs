namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// 数据源连接器接口
/// </summary>
public interface IDataSourceConnector : IDisposable
{
    /// <summary>数据源类型</summary>
    DataSourceType DataSourceType { get; }

    /// <summary>是否支持执行查询（MongoDB 只生成不执行）</summary>
    bool CanExecute { get; }

    /// <summary>连接数据源</summary>
    Task ConnectAsync(string connectionString);

    /// <summary>获取数据结构（表/集合 + 列 + 样例数据）</summary>
    Task<DatabaseSchema> GetSchemaAsync();

    /// <summary>获取所有表名</summary>
    Task<List<string>> GetTableNamesAsync();

    /// <summary>获取指定表的样例数据</summary>
    Task<List<Dictionary<string, object?>>> GetSampleDataAsync(string tableName, int limit = 3);

    /// <summary>执行查询</summary>
    Task<QueryResult> ExecuteQueryAsync(string query);

    /// <summary>测试连接</summary>
    Task<bool> TestConnectionAsync();
}
