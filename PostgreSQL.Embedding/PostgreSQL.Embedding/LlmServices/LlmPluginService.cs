using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Reflection;
using PostgreSQL.Embedding.Common.Attributes;
using System.Runtime.Loader;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace PostgreSQL.Embedding.LlmServices
{
    public class LlmPluginService : ILlmPluginService
    {
        private readonly List<TypeInfo> _llmPluginTypeList;
        private readonly IServiceProvider _serviceProvider;
        private readonly CrudBaseService<LlmPlugin> _crudBaseService;

        public LlmPluginService(CrudBaseService<LlmPlugin> crudBaseService, IServiceProvider serviceProvider)
        {
            _crudBaseService = crudBaseService;
            _serviceProvider = serviceProvider;
            _llmPluginTypeList = GetPluginTypeList();
        }

        /// <summary>
        /// 插件信息分页查询
        /// </summary>
        /// <param name="pageSize"></param>
        /// <param name="pageIndex"></param>
        /// <returns></returns>
        public async Task<PageResult<LlmPluginModel>> GetPagedPluginListAsync(int pageSize, int pageIndex)
        {
            var pagedLlmPlugins = await _crudBaseService.GetPageList(pageSize, pageIndex);

            var pageResult = new PageResult<LlmPluginModel>();
            pageResult.TotalCount = pagedLlmPlugins.TotalCount;
            pageResult.Rows = pagedLlmPlugins.Rows.Select(x => ConvertToLlmPluginModel(x)).ToList();
            return pageResult;
        }

        /// <summary>
        /// 插件信息列表
        /// </summary>
        /// <returns></returns>
        public async Task<List<LlmPluginModel>> GetPluginListAsync()
        {
            var llmPlugins = await _crudBaseService.Repository.GetAllAsync();
            return llmPlugins.OrderBy(x => x.PluginName).Select(x => ConvertToLlmPluginModel(x)).ToList();
        }

        /// <summary>
        /// 获取插件对应类型信息
        /// </summary>
        /// <param name="externalAssemblies"></param>
        /// <returns></returns>
        public List<TypeInfo> GetPluginTypeList(IEnumerable<Assembly> externalAssemblies = null)
        {
            var assembies = AssemblyLoadContext.Default.Assemblies;
            if (externalAssemblies != null && assembies.Any())
                assembies = assembies.Concat(externalAssemblies);

            var pluginTypes = assembies.SelectMany(x => x.DefinedTypes)
                .Where(x => x.GetCustomAttribute<KernelPluginAttribute>() != null).ToList();

            return pluginTypes;
        }

        /// <summary>
        /// 获取指定插件信息
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<LlmPluginModel> GetPluginByIdAsync(long id)
        {
            var llmPlugin = await _crudBaseService.GetById(id);
            return ConvertToLlmPluginModel(llmPlugin);
        }

        /// <summary>
        /// 启用或停用插件
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task ChangePluginStatusAsync(long id, bool status)
        {
            var llmPlugin = await _crudBaseService.GetById(id);
            if ((llmPlugin.Enabled && status) || (!llmPlugin.Enabled && !status))
                return;

            llmPlugin.Enabled = status;
            await _crudBaseService.Update(llmPlugin);
        }

        private LlmPluginModel ConvertToLlmPluginModel(LlmPlugin llmPlugin)
        {
            var pluginType = _llmPluginTypeList.FirstOrDefault(x => x.FullName == llmPlugin.TypeName);

            return new LlmPluginModel()
            {
                Id = llmPlugin.Id,
                PluginName = llmPlugin.PluginName,
                PluginIntro = llmPlugin.PluginIntro,
                TypeName = llmPlugin.TypeName,
                Version = llmPlugin.PluginVersion,
                IsEnabled = llmPlugin.Enabled,
                Parameters = ConstructPluginParameters(pluginType),
                Functions = ConstructPluginFunctions(pluginType),
            };
        }

        private List<LlmPluginFunctionModel> ConstructPluginFunctions(TypeInfo pluginType)
        {
            if (pluginType == null) return [];

            var pluginFunctions = pluginType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.GetCustomAttribute<KernelFunctionAttribute>() != null)
                .ToList();

            return pluginFunctions.Select(x => new LlmPluginFunctionModel()
            {
                FunctionName = x.Name.Replace("Async", ""),
                FunctionIntro = x.GetCustomAttribute<DescriptionAttribute>()?.Description ?? x.Name.Replace("Async", ""),
                Arguments = ConstructPluginFunctionArguments(x)
            })
            .ToList();
        }

        private List<LlmPluginFunctionArgumentModel> ConstructPluginFunctionArguments(MethodInfo methodInfo)
        {
            var parameters = methodInfo.GetParameters();
            if (parameters.Length == 0) return [];

            var arguments = new List<LlmPluginFunctionArgumentModel>();
            foreach (var parameter in parameters)
            {
                if (parameter.ParameterType == typeof(Kernel) || parameter.ParameterType == typeof(CancellationToken)) continue;
                var argument = new LlmPluginFunctionArgumentModel()
                {
                    ArgumentName = parameter.Name,
                    ArgumentIntro = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? parameter.Name,
                    ArgumentType = parameter.ParameterType.Name,
                    DefaultValue = parameter.DefaultValue?.ToString(),
                };
                arguments.Add(argument);
            }

            return arguments;
        }

        private List<LlmPluginParameterModel> ConstructPluginParameters(TypeInfo pluginType)
        {
            if (pluginType == null) return [];

            var pluginParameters = pluginType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => x.GetCustomAttribute<PluginParameterAttribute>() != null)
                .ToList();

            var pluginInstance = _serviceProvider.GetService(pluginType);
            return pluginParameters.Select(x => new LlmPluginParameterModel()
            {
                ParameterName = x.Name,
                ParameterType = x.PropertyType.Name,
                ParameterIntro = x.GetCustomAttribute<PluginParameterAttribute>().Description,
                IsRequired = x.GetCustomAttribute<PluginParameterAttribute>().Required,
                DefaultValue = x.GetValue(pluginInstance)?.ToString(),
            })
            .ToList();
        }
    }
}
