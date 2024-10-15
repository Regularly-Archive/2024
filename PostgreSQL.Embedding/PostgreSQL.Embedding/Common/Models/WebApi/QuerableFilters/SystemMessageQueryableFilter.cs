using PostgreSQL.Embedding.DataAccess.Entities;
using SqlSugar;

namespace PostgreSQL.Embedding.Common.Models.WebApi.QuerableFilters
{
    public class SystemMessageQueryableFilter : IQueryableFilter<SystemMessage>
    {
        public string Keyword { get; set; }
        public bool? IsRead { get; set; }
        public string Type { get; set; }

        public ISugarQueryable<SystemMessage> Apply(ISugarQueryable<SystemMessage> queryable)
        {
            if (!string.IsNullOrEmpty(Keyword))
                queryable = queryable.Where(x => x.Title.Contains(Keyword) || x.Content.Contains(Keyword));

            if (!string.IsNullOrEmpty(Type))
                queryable = queryable.Where(x => x.Type == Type);

            if (IsRead.HasValue)
                queryable = queryable.Where(x => x.IsRead == IsRead);

            return queryable;
        }
    }
}
