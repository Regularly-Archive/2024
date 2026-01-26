using PostgreSQL.Embedding.Domain.Entities;
using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Models.WebApi.QuerableFilters
{
    public class MCPServerQueryableFilter : IQueryableFilter<MCPServer>
    {
        public long? AppId {  get; set; }

        public ISugarQueryable<MCPServer> Apply(ISugarQueryable<MCPServer> queryable)
        {
            if (AppId.HasValue)
                queryable = queryable.Where(x => x.AppId == AppId.Value);

            return queryable;
        }
    }
}
