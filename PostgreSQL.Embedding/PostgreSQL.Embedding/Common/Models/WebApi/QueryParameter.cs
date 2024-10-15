using Newtonsoft.Json;
using PostgreSQL.Embedding.DataAccess.Entities;
using SqlSugar;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Common.Models.WebApi
{
    [DataContract]
    [Serializable]
    public class QueryParameter<TEntity,TFilter> where TFilter : class, IQueryableFilter<TEntity>
    {
        [JsonProperty("pageIndex")]
        public int PageIndex { get; set; }

        [JsonProperty("pageSize")]
        public int PageSize { get; set; }

        [JsonProperty("filter")]
        public TFilter Filter { get; set; }

        [JsonProperty("sortBy")]
        public string SortBy { get; set; }

        [JsonProperty("isDescending")]
        public bool IsDescending { get; set; }

        /// <summary>
        /// 默认页数
        /// </summary>
        public const int DefaultPageIndex = 1;

        /// <summary>
        /// 默认分页大小
        /// </summary>
        public const int DefaultPageSize = 10;

        /// <summary>
        /// 默认排序字段
        /// </summary>
        public const string DefaultSortBy = nameof(BaseEntity.CreatedAt);

        /// <summary>
        /// 默认是否降序，是
        /// </summary>
        public const bool DefaultIsDescending = true;
    }

    public interface IQueryableFilter<TEntity>
    {
        ISugarQueryable<TEntity> Apply(ISugarQueryable<TEntity> queryable);
    }

    public class EmptyQueryFilter<TEntity> : IQueryableFilter<TEntity>
    {
        public ISugarQueryable<TEntity> Apply(ISugarQueryable<TEntity> queryable) => queryable;
    }
}
