using PostgreSQL.Embedding.DataAccess.Entities;
using SqlSugar;

namespace PostgreSQL.Embedding.Common.Models.WebApi.QuerableFilters
{
    public class LlmModelQueryableFilter : IQueryableFilter<LlmModel>
    {
        public string ModelName { get; set; }
        public int? ModelType { get; set; }
        public int? ServiceProvider {  get; set; }

        public ISugarQueryable<LlmModel> Apply(ISugarQueryable<LlmModel> queryable)
        {
            if (!string.IsNullOrEmpty(ModelName))
                queryable = queryable.Where(x => x.ModelName.Contains(ModelName));

            if (ModelType.HasValue)
                queryable = queryable.Where(x => x.ModelType == ModelType);

            if (ServiceProvider.HasValue)
                queryable = queryable.Where(x => x.ServiceProvider == ServiceProvider);

            return queryable;
        }
    }
}
