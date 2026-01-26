using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Infrastructure.FileStorage;
using System.Reflection;

namespace PostgreSQL.Embedding.Plugins.Abstration
{
    public class BasePlugin : IPlugin
    {
        private SSEEmitter _sseEmitter;
        private readonly HttpContext _httpContext;
        private readonly ILogger<BasePlugin> _logger;
        protected readonly IServiceProvider _serviceProvider;

        public BasePlugin(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            var httpContextAccessor = serviceProvider.GetService<IHttpContextAccessor>();
            _httpContext = httpContextAccessor.HttpContext;
            if (_httpContext != null)
                _sseEmitter = new SSEEmitter(_httpContext);

            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            _logger = loggerFactory.CreateLogger<BasePlugin>();
        }

        /// <summary>
        /// 初始化插件
        /// </summary>
        /// <param name="appId"></param>
        public virtual void Initialize(long appId)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceScope.ServiceProvider;

            var pluginRepository = serviceProvider.GetService<IRepository<LlmPlugin>>();
            var parameterRepostory = serviceProvider.GetService<IRepository<LlmAppPluginParameter>>();
            InitializePluginParameters(appId, pluginRepository, parameterRepostory);
        }

        /// <summary>
        /// 校验插件
        /// </summary>
        public bool Validate(out List<string> errorMessages)
        {
            errorMessages = new List<string>();

            // 读取插件参数
            var properties = GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => x.GetCustomAttribute<PluginParameterAttribute>() != null)
                .ToList();

            if (!properties.Any()) return true;

            foreach (var property in properties)
            {
                var propertyValue = property.GetValue(this)?.ToString();
                var pluginParameterAttribute = property.GetCustomAttribute<PluginParameterAttribute>();

                if (pluginParameterAttribute.Required && string.IsNullOrEmpty(propertyValue))
                    errorMessages.Add($"The parameter '{property.Name}' of plugin '{PluginName}' is required.");
            }

            return !errorMessages.Any();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <returns></returns>
        protected async Task EmitArtifactsAsync<T>(T data)
        {
            if (_httpContext == null) return;

            var @event = new OpenAIStreamResult() { id = Guid.NewGuid().ToString("N"), obj = "chat.artifacts" };
            @event.choices.Add(new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant", content = JsonConvert.SerializeObject(data) } });
            await _sseEmitter.EmitAsync(@event);
        }

        protected async Task<LlmArtifactResponseModel> CreateArtifactAsync(string fileName, string artifactContent, Func<string, LlmArtifactResponseModel> artifactFactory)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceScope.ServiceProvider;

            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            await File.WriteAllTextAsync(filePath, artifactContent);

            var fileStorageService = serviceProvider.GetRequiredService<IFileStorageService>();
            var storageResult = await fileStorageService.PutFileAsync("artifacts", filePath);
            File.Delete(filePath);

            return artifactFactory(storageResult.FileId);
        }

        protected async Task<string> CreateArtifactAsync(string fileName, string artifactContent, string contentType)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceScope.ServiceProvider;

            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            await File.WriteAllTextAsync(filePath, artifactContent);

            var fileStorageService = serviceProvider.GetRequiredService<IFileStorageService>();
            var storageResult = await fileStorageService.PutFileAsync("artifacts", filePath);
            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex)
            {

            }

            return storageResult.FileId;
        }

        protected async Task<LlmArtifactResponseModel> CreateArtifactsAsync(string fileName, byte[] artifactContent, Func<string, LlmArtifactResponseModel> artifactFactory)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceScope.ServiceProvider;

            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            await File.WriteAllBytesAsync(filePath, artifactContent);

            var fileStorageService = serviceProvider.GetRequiredService<IFileStorageService>();
            var storageResult = await fileStorageService.PutFileAsync("artifacts", filePath);
            File.Delete(filePath);

            return artifactFactory(storageResult.FileId);
        }

        /// <summary>
        /// 当前插件名称
        /// </summary>
        public virtual string PluginName => this.GetType().Name;

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
            var pluginParameters = parameterRepository.SqlSugarClient.Queryable<LlmAppPluginParameter>().Where(x => x.AppId == appId && x.PluginId == pluginId).ToList();
            foreach (var property in properties)
            {
                var pluginParameter = pluginParameters.FirstOrDefault(x => x.ParameterName == property.Name);
                if (pluginParameter != null)
                {
                    var propertyValue = Convert.ChangeType(pluginParameter.ParameterValue, property.PropertyType);
                    property.SetValue(this, propertyValue);
                }
            }
        }
    }

    public interface IPlugin
    {
        void Initialize(long appId);

        bool Validate(out List<string> errrorMessages);

        string PluginName { get; }
    }
}
