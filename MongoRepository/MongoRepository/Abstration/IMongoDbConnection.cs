using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoRepository.Abstration
{
    public interface IMongoDbConnection
    {
        IMongoClient DatabaseClient { get; }
        string DatabaseName { get; }
    }
}
