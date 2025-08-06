using System.ComponentModel;

namespace PostgreSQL.Embedding.Common;

public enum RerankerType
{
    [Description("BGE-Reranker")] BGE = 0,
    [Description("BM25")] BM25 = 1,
    [Description("FlashRank")] FlashRank = 2
}
