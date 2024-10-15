using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.Reflection;
using System.Runtime.Loader;

namespace PostgreSQL.Embedding.Utils
{
    public static class KernelPluginsExtensions
    {
        /// <summary>
        /// 自动扫描程序集中的插件
        /// </summary>
        /// <param name="services"></param>
        /// <param name="externalAssemblies"></param>
        /// <returns></returns>
        public static IServiceCollection RegisterLlmPlugins(this IServiceCollection services, IEnumerable<Assembly> externalAssemblies = null)
        {
            var assembies = AssemblyLoadContext.Default.Assemblies;
            if (externalAssemblies != null && assembies.Any())
                assembies = assembies.Concat(externalAssemblies);

            var pluginTypes = assembies.SelectMany(x => x.DefinedTypes)
                 .Where(x => x.GetCustomAttribute<KernelPluginAttribute>() != null).ToList();

            foreach (var pluginType in pluginTypes)
            {
                var kernelPluginAttribute = pluginType.GetCustomAttribute<KernelPluginAttribute>();
                if (!kernelPluginAttribute.Enabled) continue;

                services.AddScoped(pluginType);
            }

            Task.Run(async () => await PersistLlmPligins(services, pluginTypes));
            return services;
        }

        /// <summary>
        /// 为 Kernel 导入插件
        /// </summary>
        /// <param name="kernel"></param>
        /// <param name="serviceProvider"></param>
        /// <param name="appId"></param>
        /// <param name="externalAssemblies"></param>
        /// <returns></returns>
        public static Kernel ImportLlmPlugins(this Kernel kernel, IServiceProvider serviceProvider, long? appId = null, IEnumerable<Assembly> externalAssemblies = null)
        {
            var assembies = AssemblyLoadContext.Default.Assemblies;
            if (externalAssemblies != null && assembies.Any())
                assembies = assembies.Concat(externalAssemblies);

            var pluginTypes = assembies.SelectMany(x => x.DefinedTypes)
                .Where(x => x.GetCustomAttribute<KernelPluginAttribute>() != null).ToList();

            foreach (var pluginType in pluginTypes)
            {
                var pluginInstance = serviceProvider.GetService(pluginType);
                if (pluginInstance != null)
                {
                    var kernelPluginAttribute = pluginType.GetCustomAttribute<KernelPluginAttribute>();

                    if (!kernelPluginAttribute.Enabled) continue;
                    if (appId.HasValue)
                        (pluginInstance as IPlugin).Initialize(appId.Value);
                    kernel.Plugins.AddFromObject(pluginInstance, pluginType.Name);
                }
            }

            return kernel;
        }

        /// <summary>
        /// 持久化插件
        /// </summary>
        /// <param name="services"></param>
        /// <param name="pluginTypes"></param>
        /// <returns></returns>
        private static async Task PersistLlmPligins(IServiceCollection services, IEnumerable<Type> pluginTypes)
        {
            var serviceProvider = services.BuildServiceProvider();
            var pluginRepository = serviceProvider.GetRequiredService<IRepository<LlmPlugin>>();

            foreach (var pluginType in pluginTypes)
            {
                var kernelPluginAttribute = pluginType.GetCustomAttribute<KernelPluginAttribute>();
                if (!kernelPluginAttribute.Enabled) continue;

                var pluginInstance = serviceProvider.GetRequiredService(pluginType);
                var pluginName = (pluginInstance as IPlugin).PluginName ?? pluginType.Name;

                var persistedPlugin = await pluginRepository.FindAsync(x => x.PluginName == pluginName);
                if (persistedPlugin != null && persistedPlugin.PluginVersion != kernelPluginAttribute.Version)
                {
                    persistedPlugin.PluginIntro = kernelPluginAttribute.Description;
                    persistedPlugin.PluginName = pluginName;
                    persistedPlugin.PluginVersion = kernelPluginAttribute.Version;
                    await pluginRepository.UpdateAsync(persistedPlugin);
                }
                else if (persistedPlugin == null)
                {
                    var newPlugin = new LlmPlugin()
                    {
                        PluginIntro = kernelPluginAttribute.Description,
                        PluginName = pluginName,
                        TypeName = pluginType.FullName,
                        PluginVersion = kernelPluginAttribute.Version,
                        Enabled = true,
                    };
                    await pluginRepository.AddAsync(newPlugin);
                }
            }
        }
    }
}
