using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoRepository.Abstration
{
    public interface IMongoDbContext : IDisposable
    {
        void AddCommand(Func<IClientSessionHandle, Task> func);
        Task AddCommandAsync(Func<IClientSessionHandle, Task> func);

        int Commit(IClientSessionHandle session);
        Task<int> CommitAsync(IClientSessionHandle session);

        IClientSessionHandle StartSession();
        Task<IClientSessionHandle> StartSessionAsync();

        IMongoCollection<T> GetCollection<T>() where T : BaseEntity, new();
        Task<IMongoCollection<T>> GetCollectionAsync<T>() where T : BaseEntity, new();

        void DropCollection<T>() where T : BaseEntity, new();
        Task DropCollectionAsync<T>() where T : BaseEntity, new();

    }
}
