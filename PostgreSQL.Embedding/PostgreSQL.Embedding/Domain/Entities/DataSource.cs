using SqlSugar;
using PostgreSQL.Embedding.Infrastructure.Text2DB;

namespace PostgreSQL.Embedding.Domain.Entities;

/// <summary>
/// 数据源
/// </summary>
[SugarTable("data_sources")]
public class DataSource : BaseEntity
{
    /// <summary>数据源名称</summary>
    [SugarColumn(Length = 100)]
    public string Name { get; set; } = "";

    /// <summary>数据源类型</summary>
    public DataSourceType Type { get; set; }

    /// <summary>连接字符串 / 文件路径 / 配置 JSON</summary>
    [SugarColumn(Length = 2000)]
    public string ConnectionString { get; set; } = "";

    /// <summary>描述</summary>
    [SugarColumn(Length = 500)]
    public string Description { get; set; } = "";

    /// <summary>所属应用ID</summary>
    public long AppId { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}
