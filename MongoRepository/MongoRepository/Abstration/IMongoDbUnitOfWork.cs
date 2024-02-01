using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoRepository.Abstration
{
    public interface IMongoDbUnitOfWork : IDisposable
    {
        IClientSessionHandle BeginTransaction();
        Task<IClientSessionHandle> BeginTransactionAsync();
        bool SaveChanges(IClientSessionHandle session);
        Task<bool> SaveChangesAsync(IClientSessionHandle session);
    }
}
