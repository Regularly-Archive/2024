using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.Reflection;
using System.Runtime.Loader;

namespace PostgreSQL.Embedding.Utils
{
    public static class KernelPluginsExtensions
    {
        /// <summary>
        /// 持久化所有插件元数据到数据库（启动时调用）
        /// 从 DI 容器中获取已注册的插件类型并持久化
        /// </summary>
        public static async Task PersistAllPluginsAsync(this IServiceCollection services)
        {
            var serviceProvider = services.BuildServiceProvider();
            var pluginRepository = serviceProvider.GetRequiredService<IRepository<LlmPlugin>>();

            // 从 DI 容器获取所有已注册的插件类型
            var registeredPluginTypes = GetRegisteredPluginTypes(services);

            foreach (var pluginType in registeredPluginTypes)
            {
                var kernelPluginAttribute = pluginType.GetCustomAttribute<KernelPluginAttribute>();
                if (kernelPluginAttribute == null || !kernelPluginAttribute.Enabled) continue;

                var pluginInstance = serviceProvider.GetRequiredService(pluginType);
                var pluginName = (pluginInstance as IPlugin)?.PluginName ?? pluginType.Name;
                var isBuiltin = IsBuiltInPlugin(pluginType);

                var persistedPlugin = await pluginRepository.FindAsync(x => x.PluginName == pluginName);
                if (persistedPlugin != null)
                {
                    // 更新版本或 IsBuiltin 状态
                    if (persistedPlugin.PluginVersion != kernelPluginAttribute.Version ||
                        persistedPlugin.IsBuiltin != isBuiltin)
                    {
                        persistedPlugin.PluginIntro = kernelPluginAttribute.Description;
                        persistedPlugin.PluginName = pluginName;
                        persistedPlugin.PluginVersion = kernelPluginAttribute.Version;
                        persistedPlugin.TypeName = pluginType.FullName;
                        persistedPlugin.IsBuiltin = isBuiltin;
                        persistedPlugin.IsBuiltin = kernelPluginAttribute.Enabled;
                        await pluginRepository.UpdateAsync(persistedPlugin);
                    }
                }
                else
                {
                    // 新插件
                    var newPlugin = new LlmPlugin()
                    {
                        PluginIntro = kernelPluginAttribute.Description,
                        PluginName = pluginName,
                        TypeName = pluginType.FullName,
                        PluginVersion = kernelPluginAttribute.Version,
                        IsBuiltin = isBuiltin,
                        Enabled = kernelPluginAttribute.Enabled
                    };
                    await pluginRepository.AddAsync(newPlugin);
                }
            }
        }

        /// <summary>
        /// 从 ServiceCollection 中获取所有已注册的插件类型
        /// </summary>
        private static IEnumerable<Type> GetRegisteredPluginTypes(IServiceCollection services)
        {
            var pluginTypes = new List<Type>();

            foreach (var descriptor in services)
            {
                var implementationType = descriptor.ImplementationType;
                if (implementationType != null &&
                    implementationType.GetCustomAttribute<KernelPluginAttribute>() != null)
                {
                    pluginTypes.Add(implementationType);
                }
            }

            return pluginTypes;
        }

        /// <summary>
        /// 判断是否为 BuiltIn 插件
        /// </summary>
        private static bool IsBuiltInPlugin(Type pluginType)
        {
            return pluginType.Namespace?.StartsWith("PostgreSQL.Embedding.Plugins.BuiltIn") == true;
        }

        /// <summary>
        /// 获取所有插件类型（包含 BuiltIn 和 Custom）
        /// </summary>
        private static IEnumerable<TypeInfo> GetAllPluginTypes()
        {
            return AssemblyLoadContext.Default.Assemblies
                .Where(x => x.FullName.Contains("PostgreSQL.Embedding"))
                .SelectMany(x => x.DefinedTypes)
                .Where(x => x.GetCustomAttribute<KernelPluginAttribute>() != null);
        }

        /// <summary>
        /// 导入所有插件（BuiltIn + 当前应用的 Custom）
        /// </summary>
        /// <param name="kernel"></param>
        /// <param name="serviceProvider"></param>
        /// <param name="appId">应用 ID，传入时导入该应用的 Custom 插件</param>
        /// <returns></returns>
        public static async Task<Kernel> ImportLlmPluginsAsync(
            this Kernel kernel,
            IServiceProvider serviceProvider,
            long? appId = null)
        {
            // 1. 导入 BuiltIn 插件（从 DI 容器）
            await ImportBuiltInPluginsAsync(kernel, serviceProvider, appId);

            // 2. 导入 Custom 插件（从数据库，根据 appId）
            if (appId.HasValue)
            {
                await ImportCustomPluginsAsync(kernel, serviceProvider, appId.Value);
            }

            return kernel;
        }

        /// <summary>
        /// 导入 BuiltIn 插件（从 DI 容器）
        /// </summary>
        private static async Task ImportBuiltInPluginsAsync(
            Kernel kernel,
            IServiceProvider serviceProvider,
            long? appId)
        {
            var builtInTypes = GetBuiltInPluginTypes();

            foreach (var pluginType in builtInTypes)
            {
                var kernelPluginAttribute = pluginType.GetCustomAttribute<KernelPluginAttribute>();
                if (kernelPluginAttribute == null || !kernelPluginAttribute.Enabled) continue;

                var pluginInstance = serviceProvider.GetService(pluginType);
                if (pluginInstance == null) continue;

                if (appId.HasValue)
                    (pluginInstance as IPlugin)?.Initialize(appId.Value);

                if (!kernel.Plugins.TryGetPlugin(pluginType.Name, out _))
                {
                    kernel.Plugins.AddFromObject(pluginInstance, pluginType.Name);
                }
            }
        }

        /// <summary>
        /// 导入 Custom 插件（从数据库，根据 LlmAppPlugin 关联）
        /// </summary>
        private static async Task ImportCustomPluginsAsync(
            Kernel kernel,
            IServiceProvider serviceProvider,
            long appId)
        {
            var appPluginRepository = serviceProvider.GetRequiredService<IRepository<LlmAppPlugin>>();
            var pluginRepository = serviceProvider.GetRequiredService<IRepository<LlmPlugin>>();

            // 获取该应用所有启用的插件关联
            var appPlugins = await appPluginRepository.FindListAsync(
                x => x.AppId == appId && x.Enabled);

            foreach (var appPlugin in appPlugins)
            {
                var pluginMeta = await pluginRepository.GetAsync(appPlugin.PluginId);
                if (pluginMeta == null) continue;

                var pluginType = Type.GetType(pluginMeta.TypeName);
                if (pluginType == null) continue;

                try
                {
                    var pluginInstance = serviceProvider.GetService(pluginType);
                    if (pluginInstance == null) continue;

                    (pluginInstance as IPlugin)?.Initialize(appId);

                    if (!kernel.Plugins.TryGetPlugin(pluginMeta.PluginName, out _))
                    {
                        kernel.Plugins.AddFromObject(pluginInstance, pluginMeta.PluginName);
                    }
                }
                catch (Exception ex)
                {
                    serviceProvider.GetService<ILoggerFactory>()
                        ?.CreateLogger("LlmPlugins")
                        .LogError(ex, "Failed to load custom plugin: {PluginName}", pluginMeta.PluginName);
                }
            }
        }

        /// <summary>
        /// 获取所有 BuiltIn 插件类型
        /// </summary>
        private static IEnumerable<TypeInfo> GetBuiltInPluginTypes()
        {
            return AssemblyLoadContext.Default.Assemblies
                .Where(x => x.FullName.Contains("PostgreSQL.Embedding"))
                .SelectMany(x => x.DefinedTypes)
                .Where(x => x.Namespace?.StartsWith("PostgreSQL.Embedding.Plugins.BuiltIn") == true)
                .Where(x => x.GetCustomAttribute<KernelPluginAttribute>() != null);
        }
    }
}
