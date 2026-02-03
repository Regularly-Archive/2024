using PostgreSQL.Embedding.Domain.Entities;
using SqlSugar;

namespace PostgreSQL.Embedding.Infrastructure.DataAccess
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加数据访问层服务
        /// </summary>
        public static IServiceCollection AddDataAccess(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // 注册 SqlSugar 客户端
            services.AddScoped<ISqlSugarClient>(sp =>
            {
                var connectionString = configuration.GetConnectionString("Default")
                    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

                return new SqlSugarClient(new ConnectionConfig
                {
                    DbType = DbType.PostgreSQL,
                    InitKeyType = InitKeyType.Attribute,
                    IsAutoCloseConnection = true,
                    ConnectionString = connectionString
                });
            });

            // 注册数据隔离服务
            services.AddScoped<IDataIsolationService, DataIsolationService>();

            // 注册 Repository 代理工厂
            services.AddScoped<IRepositoryProxyFactory, RepositoryProxyFactory>();

            // 注册通用仓储和服务（使用工厂模式包装代理）
            services.AddScoped(typeof(SimpleClient<>));
            services.AddScoped(typeof(CrudBaseService<>));

            // 注册 Repository，使用工厂包装
            services.RegisterRepositoryProxies(
                typeof(BaseEntity).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BaseEntity)))
                .ToArray()
            );

            return services;
        }

        /// <summary>
        /// 注册所有实体类型的 Repository 代理
        /// </summary>
        public static IServiceCollection RegisterRepositoryProxies(
            this IServiceCollection services,
            params Type[] entityTypes)
        {
            // 在同一方法内，需要先创建工厂实例
            var proxyFactory = new RepositoryProxyFactory(
                services.BuildServiceProvider().GetRequiredService<IDataIsolationService>(),
                services.BuildServiceProvider().GetRequiredService<IHttpContextAccessor>());

            foreach (var entityType in entityTypes)
            {
                var repositoryInterface = typeof(IRepository<>).MakeGenericType(entityType);
                var repositoryType = typeof(Repository<>).MakeGenericType(entityType);

                services.Add(new ServiceDescriptor(
                    repositoryInterface,
                    sp =>
                    {
                        var concreteRepository = Activator.CreateInstance(repositoryType,
                            sp.GetRequiredService<ISqlSugarClient>(),
                            sp.GetRequiredService<IHttpContextAccessor>());

                        var method = typeof(IRepositoryProxyFactory)
                            .GetMethod(nameof(IRepositoryProxyFactory.CreateProxy))
                            ?.MakeGenericMethod(entityType);

                        return method?.Invoke(proxyFactory, new[] { concreteRepository })!;
                    },
                    ServiceLifetime.Scoped));
            }

            return services;
        }
    }
}
