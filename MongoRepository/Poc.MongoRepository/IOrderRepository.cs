using MongoRepository.Abstration;
using MongoRepository.Core;
using Poc.MongoRepository.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poc.MongoRepository
{
    public interface IOrderRepository : IMongoDbRepository<Order>
    {

    }

    public class OrderRepository : MongoDbRepository<Order>, IOrderRepository
    {
        public OrderRepository(IMongoDbContext dbContext) : base(dbContext)
        {

        }
    }
}
