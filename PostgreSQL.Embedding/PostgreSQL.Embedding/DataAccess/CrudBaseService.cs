using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess.Entities;
using SqlSugar;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.DataAccess
{
    public class CrudBaseService<T> where T : BaseEntity, new()
    {
        public ISqlSugarClient SqlSugarClient => _repository.SqlSugarClient;
        public IRepository<T> Repository => _repository;

        private readonly IRepository<T> _repository;
        public CrudBaseService(IRepository<T> repository)
        {
            _repository = repository;
        }

        public Task<T> CreateAsync(T entity) => _repository.AddAsync(entity);

        public Task UpdateAsync(T entity) => _repository.UpdateAsync(entity);

        public Task DeleteAsync(string ids)
        {
            var keys = ids.Split(',').Select(x => long.Parse(x)).ToList();
            return _repository.DeleteAsync(x => keys.Contains(x.Id));
        }

        public Task<T> GetByIdAsync(long id) => _repository.GetAsync(id);

        public async Task<PagedResult<T>> GetPagedListAsync(int pageSize, int pageIndex)
        {
            var total = await _repository.CountAsync();
            var list = (await _repository.PaginateAsync(null, pageIndex, pageSize)).OrderByDescending(x => x.CreatedAt).ToList();
            return new PagedResult<T> { TotalCount = total, Rows = list };
        }

        public Task<PagedResult<T>> GetPagedListAsync<TQueryableFilter>(QueryParameter<T, TQueryableFilter> queryParameter, ISugarQueryable<T> queryable = null) where TQueryableFilter : class, IQueryableFilter<T>
        {
            return _repository.PaginateAsync(queryParameter, queryable);
        }

        public Task<List<T>> GetListAsync<TQueryableFilter>(TQueryableFilter filter, ISugarQueryable<T> queryable = null) where TQueryableFilter : class, IQueryableFilter<T>
        {
            return _repository.FindListAsync(filter, queryable);
        }
    }
}
