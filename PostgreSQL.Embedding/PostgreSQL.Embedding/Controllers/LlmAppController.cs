using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using SqlSugar;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LlmAppController : CrudBaseController<LlmApp>
    {
        private readonly ILlmPluginService _pluginService;
        private readonly IRepository<LlmAppKnowledge> _appKnowledgeRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly IRepository<LlmApp> _llmAppRepository;
        private readonly IRepository<LlmAppPlugin> _llmAppPluginRepository;
        private readonly IRepository<LlmAppPluginParameter> _llmAppPluginParameterRepository;
        public LlmAppController(
            ILlmPluginService llmPluginService,
            CrudBaseService<LlmApp> crudBaseService,
            IRepository<LlmAppPlugin> llmAppPluginRepository,
            IRepository<LlmAppPluginParameter> llmAppPluginParameterRepository,
            IRepository<LlmAppKnowledge> appKnowledgeRepository,
            IRepository<KnowledgeBase> knowledgeBaseRepository,
            IRepository<LlmApp> llmAppRepository
            ) : base(crudBaseService)
        {
            _llmAppPluginRepository = llmAppPluginRepository;
            _llmAppPluginParameterRepository = llmAppPluginParameterRepository;
            _appKnowledgeRepository = appKnowledgeRepository;
            _knowledgeBaseRepository = knowledgeBaseRepository;
            _llmAppRepository = llmAppRepository;
            _pluginService = llmPluginService;
        }

        [HttpGet("{id}/knowledges")]
        public async Task<JsonResult> GetKnowledgeBasesByApp(long id)
        {
            var appKnowledges = await _appKnowledgeRepository.FindAsync(x => x.AppId == id);
            var knowledgeIds = appKnowledges.Select(x => x.KnowledgeBaseId).ToList();
            var knowledgeBases = await _knowledgeBaseRepository.FindAsync(x => knowledgeIds.Contains(x.Id));
            return ApiResult.Success(knowledgeBases);
        }

        [HttpGet("{id}/knowledges/paginate")]
        public async Task<JsonResult> GetKnowledgeBasesByAppPage(long id, int pageIndex, int pageSize)
        {
            // 查询当前应用关联的知识库
            var appKnowledges = await _appKnowledgeRepository.FindAsync(x => x.AppId == id);
            var knowledgeIds = appKnowledges.Select(x => x.KnowledgeBaseId).ToList();

            var totalCount = await _knowledgeBaseRepository.CountAsync(x => knowledgeIds.Contains(x.Id));
            var knowledgeBases = await _knowledgeBaseRepository.PaginateAsync(x => knowledgeIds.Contains(x.Id), pageIndex, pageSize);
            return ApiResult.Success(new PageResult<KnowledgeBase> { Rows = knowledgeBases, TotalCount = totalCount });
        }

        [HttpPost("{id}/knowledges")]
        public async Task<JsonResult> AddAppKnowledges(long id, [FromBody] List<long> knowledgeBaseIds)
        {
            var appKnowledges = await _appKnowledgeRepository.FindAsync(x => x.AppId == id);
            var appKnowledgeIds = appKnowledges.Select(x => x.KnowledgeBaseId).ToList();

            var knowledgeIdList = knowledgeBaseIds.Concat(appKnowledgeIds);
            var knowledges = await _knowledgeBaseRepository.FindAsync(x => knowledgeIdList.Contains(x.Id));

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
        public override async Task<JsonResult> SelectById(long id)
        {
            var app = await _llmAppRepository.GetAsync(id);
            var appKnowledges = await _appKnowledgeRepository.FindAsync(x => x.AppId == id);
            var knowledgeIds = appKnowledges.Select(x => x.KnowledgeBaseId).ToList();
            app.KnowledgeBaseIds = knowledgeIds;
            return ApiResult.Success(app);
        }

        // Todo: 两者行为不一致
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
                IsEnabled = lp.Enabled
            })
            .ToList();

            var totalCount = query.Count();
            var pluginList = query.Skip((pageIndex - 1) * pageSize).Take(pageSize);

            // 查询插件参数信息
            var pluginIds = pluginList.Select(x => x.Id).ToList();
            var llmAppPluginParameters = await _llmAppPluginParameterRepository.FindAsync(x => x.AppId == appId && pluginIds.Contains(x.PluginId));
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

            var pagedResult = new PageResult<LlmPluginModel>() { Rows = pluginList.ToList(), TotalCount = totalCount };
            return ApiResult.Success(pagedResult);
        }

        [HttpPost("{appId}/plugins")]
        public async Task<JsonResult> AddAppPlugins(long appId, List<long> pluginIds)
        {
            var appPlugins = await _llmAppPluginRepository.FindAsync(x => pluginIds.Contains(x.PluginId) && x.AppId == appId);
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
            var appPlugin = await _llmAppPluginRepository.SingleOrDefaultAsync(x => x.AppId == appId && x.PluginId == pluginId);
            if (appPlugin == null)
                throw new Exception("当前应用尚未关联对应插件");

            var appPluginParamters = await _llmAppPluginParameterRepository.FindAsync(x => x.AppId == appId && x.PluginId == pluginId);
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
            var appPlugin = await _llmAppPluginRepository.SingleOrDefaultAsync(x => x.AppId == appId && x.PluginId == pluginId);
            if (appPlugin == null)
                throw new Exception("当前应用尚未关联对应插件");

            var pluginInstance = await _pluginService.GetPluginByIdAsync(pluginId);
            if (pluginInstance.Parameters == null || !pluginInstance.Parameters.Any())
                return ApiResult.Success(Enumerable.Empty<LlmPluginParameterModel>());

            var appPluginParameters = await _llmAppPluginParameterRepository.FindAsync(x => x.AppId == appId && x.PluginId == pluginId);
            foreach (var parameterModel in pluginInstance.Parameters)
            {
                var appPluginParameter = appPluginParameters.FirstOrDefault(x => x.ParameterName == parameterModel.ParameterName);
                if (appPluginParameter != null)
                    parameterModel.ParameterValue = appPluginParameter.ParameterValue;
            }

            return ApiResult.Success(pluginInstance.Parameters ?? []);
        }

    }
}
