using MongoDB.Driver.Core.Configuration;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoRepository.Abstration;
using MongoRepository.Configuration;
using Microsoft.Extensions.Options;

namespace MongoRepository.Core
{
    public class MongoDbConnection : IMongoDbConnection
    {
        public MongoDbConnection(MongoDbConfiguration config)
        {
            var mongoUrl = new MongoUrl(config.Url);
            var clientSettings = MongoClientSettings.FromUrl(mongoUrl);
            DatabaseClient = new MongoClient(clientSettings);
            DatabaseName = mongoUrl.DatabaseName;
        }

        public IMongoClient DatabaseClient { get; private set; }

        public string DatabaseName { get; private set; }
    }
}
