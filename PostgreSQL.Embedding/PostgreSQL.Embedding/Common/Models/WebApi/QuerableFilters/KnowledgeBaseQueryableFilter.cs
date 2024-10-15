using PostgreSQL.Embedding.DataAccess.Entities;
using SqlSugar;

namespace PostgreSQL.Embedding.Common.Models.WebApi.QuerableFilters
{
    public class KnowledgeBaseQueryableFilter : IQueryableFilter<KnowledgeBase>
    {
        public string Keyword { get; set; }

        public string EmbeddingModel { get; set; }

        public ISugarQueryable<KnowledgeBase> Apply(ISugarQueryable<KnowledgeBase> queryable)
        {
            if (!string.IsNullOrEmpty(Keyword))
                queryable = queryable.Where(x => x.Name.Contains(Keyword) || x.Intro.Contains(Keyword));

            if (!string.IsNullOrEmpty(EmbeddingModel))
                queryable = queryable.Where(x => x.EmbeddingModel == EmbeddingModel);

            return queryable;
        }
    }
}
