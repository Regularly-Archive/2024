using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Domain.Models.WebApi.QuerableFilters;
using System.Reflection;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface ILlmPluginService
    {
        Task<PagedResult<LlmPluginModel>> GetPagedPluginListAsync(QueryParameter<LlmPlugin, PluginQueryableFilter> queryParameter);

        Task<List<LlmPluginModel>> GetPluginListAsync(PluginQueryableFilter filter);

        Task<LlmPluginModel> GetPluginByIdAsync(long id);

        List<TypeInfo> GetPluginTypeList(IEnumerable<Assembly> externalAssemblies = null);

        Task ChangePluginStatusAsync(long id, bool status);
    }
}
