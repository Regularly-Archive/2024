namespace PostgreSQL.Embedding.Infrastructure.Text2DB;

/// <summary>
/// Text2DB 服务 - 统一入口
/// </summary>
public class Text2DBService
{
    private readonly Dictionary<DataSourceType, Func<IDataSourceConnector>> _connectorFactories;
    private readonly Dictionary<DataSourceType, Func<IQueryGenerator>> _generatorFactories;

    public Text2DBService()
    {
        _connectorFactories = new Dictionary<DataSourceType, Func<IDataSourceConnector>>
        {
            { DataSourceType.MySQL, () => new MySqlConnector() },
            { DataSourceType.PostgreSQL, () => new PostgreSqlConnector() },
            { DataSourceType.MongoDB, () => new MongoDbConnector() }
        };

        _generatorFactories = new Dictionary<DataSourceType, Func<IQueryGenerator>>
        {
            { DataSourceType.MySQL, () => new SqlQueryGenerator(DataSourceType.MySQL) },
            { DataSourceType.PostgreSQL, () => new SqlQueryGenerator(DataSourceType.PostgreSQL) },
            { DataSourceType.MongoDB, () => new MongoDbQueryGenerator() }
        };
    }

    /// <summary>
    /// 创建连接器
    /// </summary>
    public IDataSourceConnector CreateConnector(DataSourceType type)
    {
        if (!_connectorFactories.TryGetValue(type, out var factory))
        {
            throw new NotSupportedException($"DataSourceType {type} is not supported");
        }
        return factory();
    }

    /// <summary>
    /// 创建查询生成器
    /// </summary>
    public IQueryGenerator CreateQueryGenerator(DataSourceType type)
    {
        if (!_generatorFactories.TryGetValue(type, out var factory))
        {
            throw new NotSupportedException($"DataSourceType {type} is not supported");
        }
        return factory();
    }

    /// <summary>
    /// 获取支持的数据源类型
    /// </summary>
    public IEnumerable<DataSourceType> GetSupportedTypes()
    {
        return _connectorFactories.Keys;
    }
}
