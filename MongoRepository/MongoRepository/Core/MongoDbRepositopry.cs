using Mapster;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoRepository.Abstration;
using System.Linq.Expressions;

namespace MongoRepository.Core
{
    public class MongoDbRepository<T> : IMongoDbRepository<T> where T : BaseEntity, new()
    {
        private readonly IMongoDbContext _dbContext;
        private readonly IMongoCollection<T> _dbSet;
        public MongoDbRepository(IMongoDbContext mongoDbContext)
        {
            _dbContext = mongoDbContext;
            _dbSet = mongoDbContext.GetCollection<T>();
        }

        public async Task AddAsync(T entity, IClientSessionHandle session = null)
        {
            entity.CratedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;

            if (session == null)
                await _dbSet.InsertOneAsync(entity);
            else
                await _dbContext.AddCommandAsync(async session => await _dbSet.InsertOneAsync(entity));
        }

        public Task<long> CountAsync(FilterDefinition<T> filterDefinition)
        {
            return _dbSet.CountDocumentsAsync(filterDefinition);
        }

        public Task<long> CountAsync(Expression<Func<T, bool>> predicate)
        {
            throw new NotImplementedException();
        }

        public async Task DeleteAsync(string id, IClientSessionHandle session = null)
        {
            var filterDefinition = Builders<T>.Filter.Eq("_id", new ObjectId(id));
            if (session == null)
                await _dbSet.DeleteOneAsync(filterDefinition);
            else
                await _dbContext.AddCommandAsync(async (session) => await _dbSet.DeleteOneAsync(filterDefinition));
        }

        public async Task DeleteAsync(FilterDefinition<T> filterDefinition, IClientSessionHandle session = null)
        {
            if (session == null)
                await _dbSet.DeleteManyAsync(filterDefinition);
            else
                await _dbContext.AddCommandAsync(async (session) => await _dbSet.DeleteManyAsync(filterDefinition));
        }

        public async Task DeleteAsync(Expression<Func<T, bool>> predicate, IClientSessionHandle session = null)
        {
            if (session == null)
                await _dbSet.DeleteManyAsync(predicate);
            else
                await _dbContext.AddCommandAsync(async (session) => await _dbSet.DeleteManyAsync(predicate));
        }

        public async Task UpdateAsync(T entity, IClientSessionHandle session = null)
        {
            entity.UpdatedAt = DateTime.UtcNow;

            if (session == null)
                await _dbSet.ReplaceOneAsync(item => item.Id == entity.Id, entity);
            else
                await _dbContext.AddCommandAsync(async (session) => await _dbSet.ReplaceOneAsync(item => item.Id == entity.Id, entity));
        }

        public async Task UpdateAsync(Expression<Func<T, bool>> selector, T entity, IClientSessionHandle session = null)
        {
            entity.UpdatedAt = DateTime.UtcNow;

            var replaceOptions = new ReplaceOptions() { IsUpsert = true };
            if (session == null)
                await _dbSet.ReplaceOneAsync(selector, entity, replaceOptions);
            else
                await _dbContext.AddCommandAsync(async (session) => await _dbSet.ReplaceOneAsync(item => item.Id == entity.Id, entity, replaceOptions));
        }

        public async Task<bool> ExistsAsync(FilterDefinition<T> filterDefinition)
        {
            return (await _dbSet.CountDocumentsAsync(filterDefinition)) > 0;
        }

        public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
        {
            return (await _dbSet.CountDocumentsAsync(predicate)) > 0;
        }

        public async Task<List<T>> FindAsync(FilterDefinition<T> filterDefinition, SortDefinition<T> sortDefinition = null)
        {
            if (sortDefinition == null)
                return await _dbSet.Find(filterDefinition).ToListAsync();
            else
                return await _dbSet.Find(filterDefinition).Sort(sortDefinition).ToListAsync();
        }

        public Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, SortDefinition<T> sortDefinition = null)
        {
            if (sortDefinition == null)
                return Task.FromResult(_dbSet.Find(predicate).ToList());
            else
                return Task.FromResult(_dbSet.Find(predicate).Sort(sortDefinition).ToList());
        }

        public async Task<List<T>> GetAllAsync()
        {
            return await _dbSet.Find(Builders<T>.Filter.Empty).ToListAsync();
        }

        public async Task<T> GetAsync(string id)
        {
            var filterDefinition = Builders<T>.Filter.Eq("_id", new ObjectId(id));
            return await _dbSet.Find(filterDefinition).FirstOrDefaultAsync();
        }

        public async Task<List<T>> PaginateAsync(FilterDefinition<T> filterDefinition, int pageIndex, int pageSize, SortDefinition<T> sortDefinition = null)
        {
            if (sortDefinition == null)
                return await _dbSet.Find(filterDefinition).Skip((pageIndex - 1) * pageSize).Limit(pageSize).ToListAsync();
            else
                return await _dbSet.Find(filterDefinition).Sort(sortDefinition).Skip((pageIndex - 1) * pageSize).Limit(pageSize).ToListAsync();
        }

        public async Task<List<T>> PaginateAsync(Expression<Func<T, bool>> predicate, int pageIndex, int pageSize, SortDefinition<T> sortDefinition = null)
        {
            if (sortDefinition == null)
                return await _dbSet.Find(predicate).Skip((pageIndex - 1) * pageSize).Limit(pageSize).ToListAsync();
            else
                return await _dbSet.Find(predicate).Sort(sortDefinition).Skip((pageIndex - 1) * pageSize).Limit(pageSize).ToListAsync();
        }

        public async Task<T> SingleOrDefaultAsync(FilterDefinition<T> filterDefinition)
        {
            return await _dbSet.Find(filterDefinition).FirstOrDefaultAsync();
        }

        public async Task<T> SingleOrDefaultAsync(Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.Find(predicate).FirstOrDefaultAsync();
        }

        public async Task UpsertAsync(T entity, IClientSessionHandle session = null)
        {
            var exists = await ExistsAsync(x => x.Id == entity.Id);
            if (exists)
            {
                var record = await SingleOrDefaultAsync(x => x.Id == entity.Id);
                entity.Id = record.Id;
                entity.Adapt(record);
                await UpdateAsync(record, session);
            }
            else
            {
                await AddAsync(entity, session);
            }
        }

        public async Task UpsertAsync(Expression<Func<T, bool>> selector, T entity, IClientSessionHandle session = null)
        {
            var exists = await ExistsAsync(selector);
            if (exists)
            {
                var record = await SingleOrDefaultAsync(selector);
                entity.Id = record.Id;
                entity.Adapt(record);
                await UpdateAsync(selector, record, session);
            }
            else
            {
                await AddAsync(entity, session);
            }
        }
    }
}
