namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// 数据库 Schema
/// </summary>
public class DatabaseSchema
{
    /// <summary>数据源类型</summary>
    public DataSourceType Type { get; set; }

    /// <summary>表/集合列表</summary>
    public List<TableInfo> Tables { get; set; } = new();
}

/// <summary>
/// 表/集合信息
/// </summary>
public class TableInfo
{
    /// <summary>表名</summary>
    public string Name { get; set; } = "";

    /// <summary>表描述/注释</summary>
    public string Description { get; set; } = "";

    /// <summary>列信息</summary>
    public List<ColumnInfo> Columns { get; set; } = new();

    /// <summary>样例数据（可选，按需获取）</summary>
    public List<Dictionary<string, object?>> SampleData { get; set; } = new();
}

/// <summary>
/// 列信息
/// </summary>
public class ColumnInfo
{
    /// <summary>列名</summary>
    public string Name { get; set; } = "";

    /// <summary>数据类型</summary>
    public string DataType { get; set; } = "";

    /// <summary>描述/注释</summary>
    public string Description { get; set; } = "";

    /// <summary>是否可空</summary>
    public bool IsNullable { get; set; }
}

/// <summary>
/// 查询结果
/// </summary>
public class QueryResult
{
    /// <summary>查询类型</summary>
    public QueryType QueryType { get; set; }

    /// <summary>原始数据</summary>
    public object? Data { get; set; }

    /// <summary>执行时间 (毫秒)</summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>返回/影响行数</summary>
    public int RowCount { get; set; }
}
