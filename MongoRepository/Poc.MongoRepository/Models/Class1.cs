using MongoRepository.Abstration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poc.MongoRepository.Models
{
    [CollectionName("orders")]
    public class Order : BaseEntity
    {
        public string OrderNo { get; set; }
        public decimal Amount { get; set; }
        public DateTime OrderTime { get; set; }
        public Address Address { get; set; }
        public string Remark { get; set; }
    }

    public class Address
    {
        public string City { get; set; }
        public string Region { get; set; }
        public string PostalCode { get; set; }
    }
}
