using Pgvector;
using System.ComponentModel.DataAnnotations.Schema;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    public class EmbeddingItem
    {
        public int Id { get; set; }

        public string Content { get; set; }

        public Vector? Embedding { get; set; }
    }
}
