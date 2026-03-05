using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Domain.Models.WebApi.QuerableFilters;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Infrastructure.Text2DB;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Services;
using SqlSugar;

namespace PostgreSQL.Embedding.Application.Controllers.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LlmAppController : CrudBaseController<LlmApp, LlmAppQueryFilter>
    {
        private readonly ILlmPluginService _pluginService;
        private readonly IRepository<LlmAppKnowledge> _appKnowledgeRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly IRepository<LlmApp> _llmAppRepository;
        private readonly IRepository<LlmAppPlugin> _llmAppPluginRepository;
        private readonly IRepository<LlmAppPluginParameter> _llmAppPluginParameterRepository;
        private readonly IRepository<LlmAppSkill> _llmAppSkillRepository;
        private readonly ISkillService _skillService;
        private readonly IRepository<DataSource> _dataSourceRepository;
        public LlmAppController(
            ILlmPluginService llmPluginService,
            CrudBaseService<LlmApp> crudBaseService,
            IRepository<LlmAppPlugin> llmAppPluginRepository,
            IRepository<LlmAppPluginParameter> llmAppPluginParameterRepository,
            IRepository<LlmAppKnowledge> appKnowledgeRepository,
            IRepository<KnowledgeBase> knowledgeBaseRepository,
            IRepository<LlmApp> llmAppRepository,
            IRepository<LlmAppSkill> llmAppSkillRepository,
            IRepository<DataSource> dataSourceRepository,
            ISkillService skillService
            ) : base(crudBaseService)
        {
            _llmAppPluginRepository = llmAppPluginRepository;
            _llmAppPluginParameterRepository = llmAppPluginParameterRepository;
            _appKnowledgeRepository = appKnowledgeRepository;
            _knowledgeBaseRepository = knowledgeBaseRepository;
            _llmAppRepository = llmAppRepository;
            _pluginService = llmPluginService;
            _llmAppSkillRepository = llmAppSkillRepository;
            _skillService = skillService;
            _dataSourceRepository = dataSourceRepository;
        }

        [HttpGet("{id}/knowledges")]
        public async Task<JsonResult> GetKnowledgeBasesByApp(long id)
        {
            var appKnowledges = await _appKnowledgeRepository.FindListAsync(x => x.AppId == id);
            var knowledgeIds = appKnowledges.Select(x => x.KnowledgeBaseId).ToList();
            var knowledgeBases = await _knowledgeBaseRepository.FindListAsync(x => knowledgeIds.Contains(x.Id));
            return ApiResult.Success(knowledgeBases);
        }

        [HttpGet("{id}/knowledges/paginate")]
        public async Task<JsonResult> GetKnowledgeBasesByAppPage(long id, [FromQuery]QueryParameter<KnowledgeBase, EmptyQueryFilter<KnowledgeBase>> queryParameter)
        {
            // 查询当前应用关联的知识库
            var appKnowledges = await _appKnowledgeRepository.FindListAsync(x => x.AppId == id);
            var knowledgeIds = appKnowledges.Select(x => x.KnowledgeBaseId).ToList();

            var queryable = _knowledgeBaseRepository.SqlSugarClient.Queryable<KnowledgeBase>();
            queryable = queryable.Where(x => knowledgeIds.Contains(x.Id));
            if (queryParameter.Filter != null)
                queryable = queryParameter.Filter.Apply(queryable);


            var totalCount = await queryable.CountAsync();
            var knowledgeBases = await queryable.Skip((queryParameter.PageIndex - 1) * queryParameter.PageSize).Take(queryParameter.PageSize).ToListAsync();
            return ApiResult.Success(new PagedResult<KnowledgeBase> { Rows = knowledgeBases, TotalCount = totalCount });
        }

        [HttpPost("{id}/knowledges")]
        public async Task<JsonResult> AddAppKnowledges(long id, [FromBody] List<long> knowledgeBaseIds)
        {
            var appKnowledges = await _appKnowledgeRepository.FindListAsync(x => x.AppId == id);
            var appKnowledgeIds = appKnowledges.Select(x => x.KnowledgeBaseId).ToList();

            var knowledgeIdList = knowledgeBaseIds.Concat(appKnowledgeIds);
            var knowledges = await _knowledgeBaseRepository.FindListAsync(x => knowledgeIdList.Contains(x.Id));

            foreach (var knowledgeBaseId in knowledgeBaseIds)
            {
                var appKnowledge = appKnowledges.FirstOrDefault(x => x.KnowledgeBaseId == knowledgeBaseId);
                if (appKnowledge == null)
                {
                    var knowledge = knowledges.FirstOrDefault(x => x.Id == knowledgeBaseId);
                    if (!appKnowledges.Any() || knowledges.All(x => x.EmbeddingModel == knowledge.EmbeddingModel))
                    {
                        await _appKnowledgeRepository.AddAsync(new LlmAppKnowledge { AppId = id, KnowledgeBaseId = knowledgeBaseId });
                    }
                    else
                    {
                        throw new Exception("同一应用关联的知识库，其向量模型必须一致");
                    }

                }
            }

            return ApiResult.Success<object>(null);
        }

        [HttpDelete("{appId}/knowledges/{knowledgeBaseId}")]
        public async Task<JsonResult> DeleteAppKnowledges(long appId, long knowledgeBaseId)
        {
            await _appKnowledgeRepository.DeleteAsync(x => x.AppId == appId && x.KnowledgeBaseId == knowledgeBaseId);
            return ApiResult.Success<object>(null);
        }

        [HttpGet("{id}")]
        public override async Task<JsonResult> SelectByIdAsync(long id)
        {
            var app = await _llmAppRepository.GetAsync(id);
            var appKnowledges = await _appKnowledgeRepository.FindListAsync(x => x.AppId == id);
            var knowledgeIds = appKnowledges.Select(x => x.KnowledgeBaseId).ToList();
            app.KnowledgeBaseIds = knowledgeIds;
            return ApiResult.Success(app);
        }

        [HttpGet("{appId}/plugins/paginate")]
        public async Task<JsonResult> GetPluginsByApp(long appId, int pageIndex, int pageSize)
        {
            // 查询插件信息
            var db = _llmAppPluginRepository.SqlSugarClient;
            var query = db.Queryable<LlmAppPlugin, LlmPlugin>((lap, lp) => new object[] {
                JoinType.Left, lap.PluginId == lp.Id,
            })
            .Where((lap, lp) => lap.AppId == appId)
            .Select((lap, lp) => new LlmPluginModel()
            {
                Id = lp.Id,
                PluginName = lp.PluginName,
                PluginIntro = lp.PluginIntro,
                Version = lp.PluginVersion,
                Enabled = lap.Enabled
            })
            .ToList();

            var totalCount = query.Count();
            var pluginList = query.Skip((pageIndex - 1) * pageSize).Take(pageSize);

            // 查询插件参数信息
            var pluginIds = pluginList.Select(x => x.Id).ToList();
            var llmAppPluginParameters = await _llmAppPluginParameterRepository.FindListAsync(x => x.AppId == appId && pluginIds.Contains(x.PluginId));
            foreach (var plugin in pluginList)
            {
                // 从插件实例中获取参数信息
                var pluginInstance = await _pluginService.GetPluginByIdAsync(plugin.Id);

                if (pluginInstance == null || !pluginInstance.Parameters.Any()) continue;

                plugin.Parameters = pluginInstance.Parameters.ToList();

                foreach (var parameter in plugin.Parameters)
                {
                    // 如果插件配置了参数，则使用配置参数覆盖默认参数
                    var appPluginParameter = llmAppPluginParameters.FirstOrDefault(x => x.PluginId == plugin.Id && x.AppId == appId & x.ParameterName == parameter.ParameterName);
                    if (appPluginParameter != null)
                        parameter.ParameterValue = appPluginParameter.ParameterValue;
                }
            }

            var pagedResult = new PagedResult<LlmPluginModel>() { Rows = pluginList.ToList(), TotalCount = totalCount };
            return ApiResult.Success(pagedResult);
        }

        [HttpPost("{appId}/plugins")]
        public async Task<JsonResult> AddAppPlugins(long appId, List<long> pluginIds)
        {
            var appPlugins = await _llmAppPluginRepository.FindListAsync(x => pluginIds.Contains(x.PluginId) && x.AppId == appId);
            foreach (var pluginId in pluginIds)
            {
                var appPluguin = appPlugins.FirstOrDefault(x => x.PluginId == pluginId);
                if (appPluguin == null)
                    await _llmAppPluginRepository.AddAsync(new LlmAppPlugin() { AppId = appId, PluginId = pluginId });
            }

            return ApiResult.Success<object>(null);
        }

        [HttpDelete("{appId}/plugins/{pluginId}")]
        public async Task<JsonResult> DeleteAppPlugins(long appId, long pluginId)
        {
            await _llmAppPluginRepository.DeleteAsync(x => x.AppId == appId && x.PluginId == pluginId);
            await _llmAppPluginParameterRepository.DeleteAsync(x => x.AppId == appId && x.PluginId == pluginId);
            return ApiResult.Success<object>(null);
        }

        [HttpPut("{appId}/plugins/{pluginId}/parameters")]
        public async Task<JsonResult> SetAppPluginParameters(long appId, long pluginId, List<LlmPluginParameterModel> parameters)
        {
            var appPlugin = await _llmAppPluginRepository.FindAsync(x => x.AppId == appId && x.PluginId == pluginId);
            if (appPlugin == null)
                throw new Exception("当前应用尚未关联对应插件");

            var appPluginParamters = await _llmAppPluginParameterRepository.FindListAsync(x => x.AppId == appId && x.PluginId == pluginId);
            foreach (var parameter in parameters)
            {
                var appPluginParamster = appPluginParamters.FirstOrDefault(x => x.ParameterName == parameter.ParameterName);
                if (appPluginParamster == null && !string.IsNullOrEmpty(parameter.ParameterValue))
                {
                    await _llmAppPluginParameterRepository.AddAsync(new LlmAppPluginParameter()
                    {
                        AppId = appId,
                        PluginId = pluginId,
                        ParameterName = parameter.ParameterName,
                        ParameterValue = parameter.ParameterValue,
                    });
                }
                else
                {
                    if (!string.IsNullOrEmpty(parameter.ParameterValue))
                    {
                        appPluginParamster.ParameterValue = parameter.ParameterValue;
                        await _llmAppPluginParameterRepository.UpdateAsync(appPluginParamster);
                    }
                }
            }

            return ApiResult.Success<object>(null);
        }

        [HttpGet("{appId}/plugins/{pluginId}/parameters")]
        public async Task<JsonResult> GetAppPluginParameters(long appId, long pluginId)
        {
            var appPlugin = await _llmAppPluginRepository.FindAsync(x => x.AppId == appId && x.PluginId == pluginId);
            if (appPlugin == null)
                throw new Exception("当前应用尚未关联对应插件");

            var pluginInstance = await _pluginService.GetPluginByIdAsync(pluginId);
            if (pluginInstance.Parameters == null || !pluginInstance.Parameters.Any())
                return ApiResult.Success(Enumerable.Empty<LlmPluginParameterModel>());

            var appPluginParameters = await _llmAppPluginParameterRepository.FindListAsync(x => x.AppId == appId && x.PluginId == pluginId);
            foreach (var parameterModel in pluginInstance.Parameters)
            {
                var appPluginParameter = appPluginParameters.FirstOrDefault(x => x.ParameterName == parameterModel.ParameterName);
                if (appPluginParameter != null)
                    parameterModel.ParameterValue = appPluginParameter.ParameterValue;
            }

            return ApiResult.Success(pluginInstance.Parameters ?? []);
        }

        [HttpPut("{appId}/plugins/{pluginId}/status")]
        public async Task<JsonResult> UpdateAppPluginStatus(long appId, long pluginId, [FromQuery]bool enabled)
        {
            var appPlugin = await _llmAppPluginRepository.FindAsync(x => x.AppId == appId && x.PluginId == pluginId);
            if (appPlugin == null)
                throw new Exception("当前应用尚未关联对应插件");

            if (appPlugin.Enabled && enabled || !appPlugin.Enabled && !enabled) 
                return ApiResult.Success(appPlugin);

            appPlugin.Enabled = enabled;
            await _llmAppPluginRepository.UpdateAsync(appPlugin);

            return ApiResult.Success(appPlugin);
        }

        #region Skills

        [HttpGet("{appId}/skills/paginate")]
        public async Task<JsonResult> GetSkillsByApp(long appId, int pageIndex = 1, int pageSize = 10)
        {
            var totalCount = await _llmAppSkillRepository.CountAsync(x => x.AppId == appId);
            var skills = await _llmAppSkillRepository.PaginateAsync(x => x.AppId == appId, pageIndex, pageSize);
            return ApiResult.Success(new PagedResult<LlmAppSkill> { Rows = skills, TotalCount = totalCount });
        }

        [HttpPost("{appId}/skills")]
        public async Task<JsonResult> AddAppSkill(long appId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("请上传有效的 ZIP 文件");

            if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("仅支持 ZIP 格式的文件");

            var skill = await _skillService.ImportSkillAsync(appId, file.OpenReadStream());
            return ApiResult.Success(skill);
        }

        [HttpDelete("{appId}/skills/{skillId}")]
        public async Task<JsonResult> DeleteAppSkill(long appId, long skillId)
        {
            await _skillService.DeleteSkillAsync(appId, skillId);
            return ApiResult.Success<object>(null);
        }

        #endregion

        #region DataSources

        [HttpGet("{appId}/datasources/paginate")]
        public async Task<JsonResult> GetDataSourcesByApp(long appId, int pageIndex = 1, int pageSize = 10)
        {
            var totalCount = await _dataSourceRepository.CountAsync(x => x.AppId == appId);
            var dataSources = await _dataSourceRepository.PaginateAsync(x => x.AppId == appId, pageIndex, pageSize);
            return ApiResult.Success(new PagedResult<DataSource> { Rows = dataSources, TotalCount = totalCount });
        }

        [HttpGet("{appId}/datasources")]
        public async Task<JsonResult> GetDataSourcesByApp(long appId)
        {
            var dataSources = await _dataSourceRepository.FindListAsync(x => x.AppId == appId && x.IsEnabled == true);
            return ApiResult.Success(dataSources);
        }

        [HttpGet("{appId}/datasources/{id}")]
        public async Task<JsonResult> GetDataSourceById(long appId, long id)
        {
            var dataSource = await _dataSourceRepository.FindAsync(x => x.Id == id && x.AppId == appId);
            return ApiResult.Success(dataSource);
        }

        [HttpPost("{appId}/datasources")]
        public async Task<JsonResult> AddDataSource(long appId, [FromBody] DataSource dataSource)
        {
            dataSource.AppId = appId;
            dataSource.IsEnabled = true;
            await _dataSourceRepository.AddAsync(dataSource);
            return ApiResult.Success(dataSource);
        }

        [HttpPut("{appId}/datasources/{id}")]
        public async Task<JsonResult> UpdateDataSource(long appId, long id, [FromBody] DataSource dataSource)
        {
            var existing = await _dataSourceRepository.FindAsync(x => x.Id == id && x.AppId == appId);
            if (existing == null)
            {
                return ApiResult.Failure("数据源不存在");
            }

            existing.Name = dataSource.Name;
            existing.Type = dataSource.Type;
            existing.ConnectionString = dataSource.ConnectionString;
            existing.Description = dataSource.Description;

            await _dataSourceRepository.UpdateAsync(existing);
            return ApiResult.Success(existing);
        }

        [HttpDelete("{appId}/datasources/{id}")]
        public async Task<JsonResult> DeleteDataSource(long appId, long id)
        {
            var dataSource = await _dataSourceRepository.FindAsync(x => x.Id == id && x.AppId == appId);
            if (dataSource == null)
            {
                return ApiResult.Failure("数据源不存在");
            }

            await _dataSourceRepository.DeleteAsync(x => x.Id == id && x.AppId == appId);
            return ApiResult.Success<object>(null);
        }

        [HttpPut("{appId}/datasources/{id}/toggle")]
        public async Task<JsonResult> ToggleDataSource(long appId, long id)
        {
            var dataSource = await _dataSourceRepository.FindAsync(x => x.Id == id && x.AppId == appId);
            if (dataSource == null)
            {
                return ApiResult.Failure("数据源不存在");
            }

            dataSource.IsEnabled = !dataSource.IsEnabled;
            dataSource.UpdatedAt = DateTime.UtcNow;

            await _dataSourceRepository.UpdateAsync(dataSource);
            return ApiResult.Success(dataSource);
        }

        #endregion

        public override Task<JsonResult> GetByPageAsync(QueryParameter<LlmApp, LlmAppQueryFilter> queryParameter)
        {
            return base.GetByPageAsync(queryParameter);
        }
    }
}
