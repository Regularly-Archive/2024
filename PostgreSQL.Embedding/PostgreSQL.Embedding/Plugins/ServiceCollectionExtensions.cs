using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Utils;

namespace PostgreSQL.Embedding.Plugins
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加所有插件（启动时扫描并注册所有插件）
        /// </summary>
        public static IServiceCollection AddPlugins(
            this IServiceCollection services,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            // 扫描并注册所有插件（BuiltIn + Custom）
            var pluginTypes = GetAllPluginTypes();

            foreach (var pluginType in pluginTypes)
            {
                services.AddScoped(pluginType);
            }

            // 依赖服务
            services.AddMcpClientFactory();
            services.AddPythonRuntime(configuration);

            return services;
        }

        /// <summary>
        /// 获取所有插件类型（扫描程序集）
        /// </summary>
        private static IEnumerable<Type> GetAllPluginTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => x.FullName.Contains("PostgreSQL.Embedding"))
                .SelectMany(x => x.GetTypes())
                .Where(x => x.GetCustomAttributes(typeof(KernelPluginAttribute), false).Length > 0)
                .Where(x => x.IsClass && !x.IsAbstract);
        }

        /// <summary>
        /// 添加 MCP 客户端工厂
        /// </summary>
        public static IServiceCollection AddMcpClientFactory(this IServiceCollection services)
        {
            services.AddScoped<McpConnectionFactory>();
            return services;
        }

        /// <summary>
        /// 添加 Python 运行时
        /// </summary>
        public static IServiceCollection AddPythonRuntime(
            this IServiceCollection services,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            CSnakeExtensions.AddPythonRuntime(services, configuration);
            return services;
        }
    }
}
