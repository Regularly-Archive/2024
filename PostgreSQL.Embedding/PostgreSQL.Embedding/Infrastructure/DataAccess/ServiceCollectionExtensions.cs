using Microsoft.Extensions.Configuration;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using SqlSugar;
using System.Text;

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

            // 注册通用仓储和服务
            services.AddScoped(typeof(SimpleClient<>));
            services.AddScoped(typeof(CrudBaseService<>));
            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

            return services;
        }
    }
}
