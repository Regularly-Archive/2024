using Mapster;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.Services;
using SqlSugar;
using System.Linq.Expressions;
using System.Reflection.Metadata;

namespace PostgreSQL.Embedding.DataAccess
{
    public interface ICrudBaseService<TEntity, TEditDto, TQueryDto> where TEntity: BaseEntity, new()
    {
        Task<TEntity> AddAsync(TEditDto editDto);
        Task UpdateAsync(TEditDto editDto);
        Task DeleteAsync(long id);
        Task DeleteAsync(Expression<Func<TEntity, bool>> predicate);
        Task UpsetAsync(TEditDto editDto);
        Task<List<TQueryDto>> GetAllAsync();
        Task<TQueryDto> GetAsync(long id);
        Task<List<TQueryDto>> FindAsync(Expression<Func<TEntity, bool>> predicate);
        Task<TQueryDto> SingleOrDefaultAsync(Expression<Func<TEntity, bool>> predicate);
        Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate = null);
        Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);
        Task<List<TQueryDto>> PaginateAsync(Expression<Func<TEntity, bool>> predicate, int pageIndex, int pageSize);
    }

    public class CrudBaseService<TEntity, TEditDto, TQueryDto> : ICrudBaseService<TEntity, TEditDto, TQueryDto>
        where TEntity : BaseEntity, new()
        where TEditDto : class, new()
        where TQueryDto : class, new()
    {
        private readonly SimpleClient<TEntity> _repository;
        private readonly IUserInfoService _userInfoService;
        public CrudBaseService(IServiceProvider serviceProvider)
        {
            _repository = serviceProvider.GetService<SimpleClient<TEntity>>();
            _userInfoService = serviceProvider.GetService<IUserInfoService>();
        }

        public async Task<TEntity> AddAsync(TEditDto editDto)
        {
            var entity = editDto.Adapt<TEntity>();
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = _userInfoService.GetCurrentUser().Identity?.Name ?? Constants.Admin;
            entity.UpdatedAt = entity.CreatedAt;
            entity.UpdatedBy = entity.CreatedBy;
            return await _repository.InsertReturnEntityAsync(entity);
        }

        public async Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate)
        {
            if (predicate == null) predicate = x => true;
            return await _repository.CountAsync(predicate);
        }

        public async Task DeleteAsync(long id)
        {
            var entity = _repository.GetById(id);
            if (entity != null) await _repository.DeleteAsync(entity);
        }

        public async Task DeleteAsync(Expression<Func<TEntity, bool>> predicate)
        {
            await _repository.DeleteAsync(predicate);
        }

        public async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate)
        {
            var count = await _repository.CountAsync(predicate);
            return count > 0;
        }

        public async Task<List<TQueryDto>> FindAsync(Expression<Func<TEntity, bool>> predicate)
        {
            var list = await _repository.GetListAsync(predicate);
            return list.Adapt<List<TQueryDto>>();
        }

        public async Task<List<TQueryDto>> GetAllAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Adapt<List<TQueryDto>>();
        }

        public async Task<TQueryDto> GetAsync(long id)
        {
            var entity = await _repository.GetByIdAsync(id);
            return entity.Adapt<TQueryDto>();
        }

        public async Task<List<TQueryDto>> PaginateAsync(Expression<Func<TEntity, bool>> predicate, int pageIndex, int pageSize)
        {
            var pageModel = new PageModel() { PageIndex = pageIndex, PageSize = pageSize };
            var list = await _repository.GetPageListAsync(predicate, pageModel);
            return list.Adapt<List<TQueryDto>>();
        }

        public async Task<TQueryDto> SingleOrDefaultAsync(Expression<Func<TEntity, bool>> predicate)
        {
            var entity = await _repository.GetSingleAsync(predicate);
            return entity.Adapt<TQueryDto>();
        }

        public async Task UpdateAsync(TEditDto editDto)
        {
            var entity = editDto.Adapt<TEntity>();
            entity.UpdatedAt = DateTime.Now;
            entity.UpdatedBy = _userInfoService.GetCurrentUser().Identity?.Name ?? Constants.Admin;
            await _repository.UpdateAsync(entity);
        }

        public async Task UpsetAsync(TEditDto editDto)
        {
            var entity = editDto.Adapt<TEntity>();
            await _repository.InsertOrUpdateAsync(entity);
        }
    }
}
