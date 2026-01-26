using Newtonsoft.Json;
using PostgreSQL.Embedding.Domain.Entities;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Domain.Models.WebApi.QuerableFilters
{
    public class LlmAppQueryFilter : IQueryableFilter<LlmApp>
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public string Keyword { get; set; }
        public int? AppType { get; set; }

        public string TextModel { get; set; }

        public SqlSugar.ISugarQueryable<LlmApp> Apply(SqlSugar.ISugarQueryable<LlmApp> queryable)
        {
            if (!string.IsNullOrEmpty(Keyword))
                queryable = queryable.Where(x => x.Name.Contains(Keyword));

            if (!string.IsNullOrEmpty(TextModel))
                queryable = queryable.Where(x => x.TextModel.Equals(TextModel));

            if (StartDate.HasValue)
                queryable = queryable.Where(x => x.CreatedAt >= StartDate.Value);

            if (EndDate.HasValue)
                queryable = queryable.Where(x => x.CreatedAt <= EndDate.Value); 

            if (AppType.HasValue)
                queryable = queryable.Where(x => x.AppType == AppType);

            return queryable;

        }
    }
}
