namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// 数据源类型
/// </summary>
public enum DataSourceType
{
    // 关系型数据库
    MySQL = 1,
    PostgreSQL = 2,
    SQLServer = 3,
    Oracle = 4,
    SQLite = 5,
    DuckDB = 6,

    // NoSQL
    MongoDB = 10,

    // 文件
    Excel = 20,
    CSV = 21,
    JSON = 22
}

/// <summary>
/// 查询类型
/// </summary>
public enum QueryType
{
    Select,
    Insert,
    Update,
    Delete,
    Drop,
    Other
}
