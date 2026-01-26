using PostgreSQL.Embedding.Domain.Entities;
using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Models.WebApi.QuerableFilters
{
    public class PluginQueryableFilter : IQueryableFilter<LlmPlugin>
    {
        public string Keyword { get; set; }

        public ISugarQueryable<LlmPlugin> Apply(ISugarQueryable<LlmPlugin> queryable)
        {
            if (!string.IsNullOrEmpty(Keyword))
                queryable = queryable.Where(x => x.PluginName.Contains(Keyword) || x.PluginIntro.Contains(Keyword));

            return queryable;
        }
    }
}
