using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum RetrievalType
    {
        [Description("向量检索")] Vectors = 0,
        [Description("全文检索")] FullText = 1
    }
}
