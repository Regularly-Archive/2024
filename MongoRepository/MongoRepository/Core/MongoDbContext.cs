using MongoDB.Driver;
using MongoRepository.Abstration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace MongoRepository.Core
{
    public class MongoDbContext : IMongoDbContext
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly List<Func<IClientSessionHandle, Task>> _commands = new List<Func<IClientSessionHandle, Task>>();

        public MongoDbContext(IMongoDbConnection mongoDbConnection)
        {
            _client = mongoDbConnection.DatabaseClient;
            _database = _client.GetDatabase(mongoDbConnection.DatabaseName);
        }

        public void AddCommand(Func<IClientSessionHandle, Task> func)
        {
            _commands.Add(func);
        }

        public Task AddCommandAsync(Func<IClientSessionHandle, Task> func)
        {
            _commands.Add(func);
            return Task.CompletedTask;
        }

        public int Commit(IClientSessionHandle session)
        {
            try
            {
                session.StartTransaction();
                foreach (var command in _commands)
                {
                    command(session);
                }

                session.CommitTransaction();
                return _commands.Count;
            }
            catch (Exception e)
            {
                session.AbortTransaction();
                return 0;
            }
            finally
            {
                _commands.Clear();
            }
        }

        public async Task<int> CommitAsync(IClientSessionHandle session)
        {
            try
            {
                session.StartTransaction();
                foreach (var command in _commands)
                {
                    await command(session);
                }

                await session.CommitTransactionAsync();
                return _commands.Count;
            }
            catch (Exception e)
            {
                await session.AbortTransactionAsync();
                return 0;
            }
            finally
            {
                _commands.Clear();
            }
        }

        public void DropCollection<T>() where T : BaseEntity, new()
        {
            var collectionName = GetCollectionName<T>();
            _database.DropCollection(collectionName);
        }

        public async Task DropCollectionAsync<T>() where T : BaseEntity, new()
        {
            var collectionName = GetCollectionName<T>();
            await _database.DropCollectionAsync(collectionName);
        }

        public IMongoCollection<T> GetCollection<T>() where T : BaseEntity, new()
        {
            var collectionName = GetCollectionName<T>();
            return _database.GetCollection<T>(collectionName);
        }

        public Task<IMongoCollection<T>> GetCollectionAsync<T>() where T : BaseEntity, new()
        {
            var collectionName = GetCollectionName<T>();
            var collection = _database.GetCollection<T>(collectionName);
            return Task.FromResult(collection);
        }

        public IClientSessionHandle StartSession()
        {
            return _client.StartSession();
        }

        public Task<IClientSessionHandle> StartSessionAsync()
        {
            return _client.StartSessionAsync();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        private string GetCollectionName<T>() where T : BaseEntity, new()
        {
            var collectionNameAttr = typeof(T).GetCustomAttribute<CollectionNameAttribute>();
            return collectionNameAttr == null ? typeof(T).Name : collectionNameAttr.Name;
        }
    }
}
