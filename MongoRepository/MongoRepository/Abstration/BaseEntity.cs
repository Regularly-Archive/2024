using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoRepository.Abstration
{
    public class BaseEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public virtual string Id { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public virtual DateTime CratedAt { get; set; }

        public virtual string CratedBy { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public virtual DateTime UpdatedAt { get; set; }

        public virtual string UpdatedBy { get; set; }
    }
}
