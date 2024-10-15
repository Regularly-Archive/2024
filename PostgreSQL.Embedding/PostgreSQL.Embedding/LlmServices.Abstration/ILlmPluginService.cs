using Masuit.Tools.Models;
using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.Common.Models.WebApi.QuerableFilters;
using PostgreSQL.Embedding.DataAccess.Entities;
using System.Reflection;

namespace PostgreSQL.Embedding.LlmServices.Abstration
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
