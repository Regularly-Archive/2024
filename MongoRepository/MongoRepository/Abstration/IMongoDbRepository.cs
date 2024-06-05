using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace MongoRepository.Abstration
{
    public interface IMongoDbRepository<T>
    {
        Task AddAsync(T entity, IClientSessionHandle session = null);
        Task UpdateAsync(T entity, IClientSessionHandle session = null);
        Task UpdateAsync(Expression<Func<T, bool>> selector, T entity, IClientSessionHandle session = null);
        Task DeleteAsync(string id, IClientSessionHandle session = null);
        Task DeleteAsync(FilterDefinition<T> filterDefinition, IClientSessionHandle session = null);
        Task DeleteAsync(Expression<Func<T, bool>> predicate, IClientSessionHandle session = null);
        Task UpsertAsync(T entity, IClientSessionHandle session = null);
        Task UpsertAsync(Expression<Func<T, bool>> selector, T entity, IClientSessionHandle session = null);
        Task<List<T>> GetAllAsync();
        Task<T> GetAsync(string id);
        Task<List<T>> FindAsync(FilterDefinition<T> filterDefinition, SortDefinition<T> sortDefinition = null);
        Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, SortDefinition<T> sortDefinition = null);
        Task<T> SingleOrDefaultAsync(FilterDefinition<T> filterDefinition);
        Task<T> SingleOrDefaultAsync(Expression<Func<T, bool>> predicate);
        Task<long> CountAsync(FilterDefinition<T> filterDefinition);
        Task<long> CountAsync(Expression<Func<T, bool>> predicate);
        Task<bool> ExistsAsync(FilterDefinition<T> filterDefinition);
        Task<bool> ExistsAsync(Expression<Func<T,bool>> predicate);
        Task<List<T>> PaginateAsync(FilterDefinition<T> filterDefinition, int pageIndex, int pageSize, SortDefinition<T> sortDefinition = null);
        Task<List<T>> PaginateAsync(Expression<Func<T, bool>> predicate, int pageIndex, int pageSize, SortDefinition<T> sortDefinition = null);

    }
}
