using PostgreSQL.Embedding.Infrastructure.Text2DB;

namespace PostgreSQL.Embedding.Domain.Models;

/// <summary>
/// 数据源列表项 DTO（不包含敏感信息）
/// </summary>
public class DataSourceDto
{
    /// <summary>主键 ID</summary>
    public long Id { get; set; }

    /// <summary>数据源名称</summary>
    public string Name { get; set; } = "";

    /// <summary>数据源类型</summary>
    public DataSourceType Type { get; set; }

    /// <summary>类型名称</summary>
    public string TypeName { get; set; } = "";

    /// <summary>描述</summary>
    public string Description { get; set; } = "";

    /// <summary>所属应用ID</summary>
    public long AppId { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }
}
