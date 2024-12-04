using DocumentFormat.OpenXml.Vml.Office;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.Services;
using SqlSugar;
using System.Linq;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.DataAccess
{
    public interface IRepository<T> where T : BaseEntity, new()
    {
        ISqlSugarClient SqlSugarClient { get; }
        Task<T> AddAsync(T entity);
        Task AddAsync(params T[] entities);
        Task UpdateAsync(T entity);
        Task DeleteAsync(long id);
        Task DeleteAsync(Expression<Func<T, bool>> predicate);
        Task<List<T>> GetAllAsync();
        Task<T> GetAsync(long id);
        Task<List<T>> FindListAsync(Expression<Func<T, bool>> predicate);
        Task<T> FindAsync(Expression<Func<T, bool>> predicate);
        Task<int> CountAsync(Expression<Func<T, bool>> predicate = null);
        Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate);
        Task<List<T>> PaginateAsync(Expression<Func<T, bool>> predicate, int pageIndex, int pageSize);
        Task<PagedResult<T>> PaginateAsync<TQueryFilter>(QueryParameter<T, TQueryFilter> queryParameter, ISugarQueryable<T> queryable = null) where TQueryFilter : class, IQueryableFilter<T>;
        Task<List<T>> FindListAsync<TQueryFilter>(TQueryFilter filter, ISugarQueryable<T> queryable = null) where TQueryFilter : class, IQueryableFilter<T>;
    }

    public class Repository<T> : SimpleClient<T>, IRepository<T> where T : BaseEntity, new()
    {
        public ISqlSugarClient SqlSugarClient => _sqlSugarClient;

        private readonly ISqlSugarClient _sqlSugarClient;
        private readonly IHttpContextAccessor _httpContextAccessor;
        public Repository(ISqlSugarClient db, IHttpContextAccessor httpContextAccessor) : base(db)
        {
            _httpContextAccessor = httpContextAccessor;
            _sqlSugarClient = db;
        }

        public Task<T> AddAsync(T entity)
        {
            EnrichBaseProperties(entity, true);

            return base.InsertReturnEntityAsync(entity);
        }

        public async Task AddAsync(params T[] entities)
        {
            foreach (var entity in entities)
            {
                EnrichBaseProperties(entity, true);
                await base.InsertReturnEntityAsync(entity);
            }
        }

        Task IRepository<T>.UpdateAsync(T entity)
        {
            EnrichBaseProperties(entity, false);
            return base.UpdateAsync(entity);
        }

        public Task DeleteAsync(long id)
        {
            return base.DeleteByIdAsync(id);
        }

        Task IRepository<T>.DeleteAsync(Expression<Func<T, bool>> predicate)
        {
            return base.DeleteAsync(predicate);
        }

        public Task<List<T>> GetAllAsync()
        {
            return base.GetListAsync();
        }

        public Task<T> GetAsync(long id)
        {
            return base.GetByIdAsync(id);
        }

        public Task<List<T>> FindListAsync(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null) predicate = x => true;
            return base.GetListAsync(predicate);
        }

        public Task<T> FindAsync(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return base.GetFirstAsync(predicate);
        }

        Task<int> IRepository<T>.CountAsync(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null) predicate = x => true;
            return base.CountAsync(predicate);
        }

        public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
        {
            var count = await base.CountAsync(predicate);
            return count > 0;
        }

        public Task<List<T>> PaginateAsync(Expression<Func<T, bool>> predicate, int pageIndex, int pageSize)
        {
            if (predicate == null) predicate = x => true;
            var pageModel = new PageModel() { PageIndex = pageIndex, PageSize = pageSize };
            return base.GetPageListAsync(predicate, pageModel);
        }

        public async Task<PagedResult<T>> PaginateAsync<TQueryFilter>(QueryParameter<T, TQueryFilter> queryParameter, ISugarQueryable<T> queryable = null) where TQueryFilter : class, IQueryableFilter<T>
        {
            queryable = queryable ?? base.AsQueryable();

            if (queryParameter.Filter != null)
                queryable = queryParameter.Filter.Apply(queryable);

            var total = await queryable.CountAsync();

            queryable = queryable.Skip((queryParameter.PageIndex - 1) * queryParameter.PageSize).Take(queryParameter.PageSize);

            if (!string.IsNullOrEmpty(queryParameter.SortBy))
                queryable = queryable.OrderByPropertyName(queryParameter.SortBy, queryParameter.IsDescending ? OrderByType.Desc : OrderByType.Asc);

            var list = await queryable.ToListAsync();
            return new PagedResult<T> { TotalCount = total, Rows = list };
        }

        public Task<List<T>> FindListAsync<TQueryFilter>(TQueryFilter filter, ISugarQueryable<T> queryable = null) where TQueryFilter : class, IQueryableFilter<T>
        {
            queryable = queryable ?? base.AsQueryable();
            if (filter != null) queryable = filter.Apply(queryable);

            return queryable.ToListAsync();
        }

        private void EnrichBaseProperties(T entity, bool isCreate)
        {
            if (isCreate)
            {
                entity.CreatedAt = DateTime.Now;
                entity.CreatedBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? Constants.Admin;
                entity.UpdatedAt = entity.CreatedAt;
                entity.UpdatedBy = entity.CreatedBy;
            }
            else
            {
                entity.UpdatedAt = DateTime.Now;
                entity.UpdatedBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? Constants.Admin;
            }
        }


    }
}
