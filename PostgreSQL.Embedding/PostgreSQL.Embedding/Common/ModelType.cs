using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum ModelType
    {
        [Description("文本生成")] TextGeneration = 0,
        [Description("向量生成")] TextEmbedding = 1
    }
}
