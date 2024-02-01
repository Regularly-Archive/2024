using MongoDB.Driver;
using MongoRepository.Abstration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoRepository.Core
{
    public class MongoDbUnitOfWork : IMongoDbUnitOfWork
    {
        private readonly IMongoDbContext _dbContext;
        public MongoDbUnitOfWork(IMongoDbContext mongoDbContext)
        {
            _dbContext = mongoDbContext;
        }

        public IClientSessionHandle BeginTransaction()
        {
            return _dbContext.StartSession();
        }

        public Task<IClientSessionHandle> BeginTransactionAsync()
        {
            return _dbContext.StartSessionAsync();
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        public bool SaveChanges(IClientSessionHandle session)
        {
            return _dbContext.Commit(session) > 0;
        }

        public async Task<bool> SaveChangesAsync(IClientSessionHandle session)
        {
            return (await _dbContext.CommitAsync(session)) > 0;
        }
    }
}
