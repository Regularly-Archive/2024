using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.DataAccess
{
    public class CrudBaseService<T> where T : BaseEntity, new()
    {
        private readonly IRepository<T> _repository;
        public CrudBaseService(IRepository<T> repository)
        {
            _repository = repository;
        }

        public Task<T> Create(T entity) => _repository.AddAsync(entity);

        public Task Update(T entity) => _repository.UpdateAsync(entity);

        public Task Delete(string ids)
        {
            var keys = ids.Split(',').Select(x => long.Parse(x)).ToList();
            return _repository.DeleteAsync(x => keys.Contains(x.Id));
        }

        public Task<T> GetById(long id) => _repository.GetAsync(id);

        public async Task<PageResult<T>> GetPageList(int pageSize, int pageIndex)
        {
            var total = await _repository.CountAsync();
            var list = await _repository.PaginateAsync(null, pageIndex, pageSize);
            return new PageResult<T> { TotalCount = total, Rows = list };
        }
    }
}
