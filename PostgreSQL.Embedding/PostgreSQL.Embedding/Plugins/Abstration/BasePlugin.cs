using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using System.Reflection;

namespace PostgreSQL.Embedding.Plugins.Abstration
{
    public class BasePlugin : IPlugin
    {
        private ILogger<BasePlugin> _logger;
        protected IServiceProvider _serviceProvider;

        public BasePlugin(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            _logger = loggerFactory.CreateLogger<BasePlugin>();
        }

        /// <summary>
        /// 初始化插件
        /// </summary>
        /// <param name="appId"></param>
        public void Initialize(long appId)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceScope.ServiceProvider;

            var pluginRepository = serviceProvider.GetService<IRepository<LlmPlugin>>();
            var parameterRepostory = serviceProvider.GetService<IRepository<LlmAppPluginParameter>>();
            InitializePluginParameters(appId, pluginRepository, parameterRepostory);
        }

        private void InitializePluginParameters(long appId, IRepository<LlmPlugin> pluginRepository, IRepository<LlmAppPluginParameter> parameterRepository)
        {
            // 读取插件信息
            var pluginInfo = pluginRepository.SqlSugarClient.Queryable<LlmPlugin>().First(x => x.PluginName == PluginName);
            if (pluginInfo == null)
            {
                _logger.LogError("The plugin '{0}' can not be found in database, please verify if it is registered.", PluginName);
                return;
            }

            // 读取插件参数
            var properties = GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => x.GetCustomAttribute<PluginParameterAttribute>() != null)
                .ToList();

            if (!properties.Any()) return;

            // 设置插件参数
            var pluginId = pluginInfo.Id;
            foreach (var property in properties)
            {
                var pluginParameter = parameterRepository.SqlSugarClient.Queryable<LlmAppPluginParameter>().First(x => x.AppId == appId && x.PluginId == pluginId && x.ParameterName == property.Name);
                if (pluginParameter != null)
                {
                    var propertyValue = Convert.ChangeType(pluginParameter.ParameterValue, property.PropertyType);
                    property.SetValue(this, propertyValue);
                }
            }
        }

        public virtual string PluginName 
        {
            get
            {
                return this.GetType().Name;
            }
        }
    }

    public interface IPlugin
    {
        void Initialize(long appId);

        string PluginName { get; }
    }
}
