using Masuit.Tools.Models;
using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.Common.Models.WebApi;
using System.Reflection;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface ILlmPluginService
    {
        Task<PageResult<LlmPluginModel>> GetPagedPluginListAsync(int pageSize, int pageIndex);

        Task<List<LlmPluginModel>> GetPluginListAsync();

        Task<LlmPluginModel> GetPluginByIdAsync(long id);

        List<TypeInfo> GetPluginTypeList(IEnumerable<Assembly> externalAssemblies = null);

        Task ChangePluginStatusAsync(long id, bool status);
    }
}
