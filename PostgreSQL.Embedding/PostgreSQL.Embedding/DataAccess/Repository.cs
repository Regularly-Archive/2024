using DocumentFormat.OpenXml.Vml.Office;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.Services;
using SqlSugar;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.DataAccess
{
    public interface IRepository<TEntity> where TEntity : BaseEntity, new()
    {
        Task<TEntity> AddAsync(TEntity entity);
        Task UpdateAsync(TEntity entity);
        Task DeleteAsync(long id);
        Task DeleteAsync(Expression<Func<TEntity, bool>> predicate);
        Task<List<TEntity>> GetAllAsync();
        Task<TEntity> GetAsync(long id);
        Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);
        Task<TEntity> SingleOrDefaultAsync(Expression<Func<TEntity, bool>> predicate);
        Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate = null);
        Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);
        Task<List<TEntity>> PaginateAsync(Expression<Func<TEntity, bool>> predicate, int pageIndex, int pageSize);
    }

    public class Repository<T> : SimpleClient<T>, IRepository<T> where T : BaseEntity, new()
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public Repository(ISqlSugarClient db, IHttpContextAccessor httpContextAccessor) : base(db)
        {
            _httpContextAccessor = httpContextAccessor;
        }
        public Task<T> AddAsync(T entity)
        {
            EnrichBaseProperties(entity, true);
            return base.InsertReturnEntityAsync(entity);
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

        public Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate)
        {
            return base.GetListAsync(predicate);
        }

        public Task<T> SingleOrDefaultAsync(Expression<Func<T, bool>> predicate)
        {
            return base.GetFirstAsync(predicate);
        }

        Task<int> IRepository<T>.CountAsync(Expression<Func<T, bool>> predicate)
        {
           return base.CountAsync(predicate);
        }

        public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
        {
            var count = await base.CountAsync(predicate);
            return count > 0;
        }

        public Task<List<T>> PaginateAsync(Expression<Func<T, bool>> predicate, int pageIndex, int pageSize)
        {
            var pageModel = new PageModel() { PageIndex = pageIndex, PageSize = pageSize };
            return base.GetPageListAsync(predicate, pageModel);
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
