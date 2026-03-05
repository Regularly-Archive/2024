using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Infrastructure.Text2DB;

namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// Text2DB 服务扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Text2DB 服务
    /// </summary>
    public static IServiceCollection AddText2DB(this IServiceCollection services)
    {
        services.AddSingleton<Text2DBService>();
        return services;
    }

    /// <summary>
    /// 添加指定类型的数据源连接器
    /// </summary>
    public static IServiceCollection AddDataSourceConnector<TConnector>(
        this IServiceCollection services,
        DataSourceType dataSourceType)
        where TConnector : class, IDataSourceConnector
    {
        services.AddKeyedSingleton<IDataSourceConnector, TConnector>(dataSourceType);
        return services;
    }
}
