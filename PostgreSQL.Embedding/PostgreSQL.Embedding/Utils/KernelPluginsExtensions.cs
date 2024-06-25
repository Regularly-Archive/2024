using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using System.Reflection;
using System.Runtime.Loader;

namespace PostgreSQL.Embedding.Utils
{
    public static class KernelPluginsExtensions
    {
        public static IServiceCollection RegisterLlmPlugins(this IServiceCollection services, IEnumerable<Assembly> externalAssemblies = null)
        {
            var assembies = AssemblyLoadContext.Default.Assemblies;
            if (externalAssemblies != null && assembies.Any())
                assembies = assembies.Concat(externalAssemblies);

            var pluginTypes = assembies.SelectMany(x => x.DefinedTypes)
                 .Where(x => x.GetCustomAttribute<KernelPluginAttribute>() != null).ToList();

            foreach (var pluginType in pluginTypes)
            {
                services.AddScoped(pluginType);
            }

            Task.Run(async () => await PersistLlmPligins(services, pluginTypes));
            return services;
        }

        public static Kernel ImportLlmPlugins(this Kernel kernel, IServiceProvider serviceProvider, IEnumerable<Assembly> externalAssemblies = null)
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
                    if (string.IsNullOrEmpty(kernelPluginAttribute.PluginName))
                        kernelPluginAttribute.PluginName = pluginType.Name;

                    kernel.Plugins.AddFromObject(pluginInstance, kernelPluginAttribute.PluginName);
                }
            }

            return kernel;
        }

        private static async Task PersistLlmPligins(IServiceCollection services, IEnumerable<Type> pluginTypes)
        {
            var serviceProvider = services.BuildServiceProvider();
            var pluginRepository = serviceProvider.GetRequiredService<IRepository<LlmPlugin>>();

            foreach (var pluginType in pluginTypes)
            {
                var kernelPluginAttribute = pluginType.GetCustomAttribute<KernelPluginAttribute>();
                var persistedPlugin = await pluginRepository.SingleOrDefaultAsync(x => x.TypeName == pluginType.FullName);
                if (persistedPlugin != null)
                {
                    persistedPlugin.PluginIntro = kernelPluginAttribute.Description;
                    persistedPlugin.PluginName = kernelPluginAttribute.PluginName ?? pluginType.Name;
                    await pluginRepository.UpdateAsync(persistedPlugin);
                } 
                else
                {
                    var newPlugin = new LlmPlugin()
                    {
                        PluginIntro = kernelPluginAttribute.Description,
                        PluginName = kernelPluginAttribute.PluginName ?? pluginType.Name,
                        TypeName = pluginType.FullName
                    };
                    await pluginRepository.AddAsync(newPlugin);
                }
            }
        }
    }
}
