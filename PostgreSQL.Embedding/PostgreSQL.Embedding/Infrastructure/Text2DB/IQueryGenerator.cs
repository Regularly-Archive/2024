namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// 查询生成器接口
/// </summary>
public interface IQueryGenerator
{
    /// <summary>数据源类型</summary>
    DataSourceType DataSourceType { get; }

    /// <summary>生成查询脚本/SQL</summary>
    Task<string> GenerateAsync(DatabaseSchema schema, string userQuestion);

    /// <summary>格式化执行结果为文本</summary>
    Task<string> FormatResultAsync(QueryResult result);
}
