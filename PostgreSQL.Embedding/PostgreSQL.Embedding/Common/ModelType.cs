using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum ModelType
    {
        [Description("文本生成")] TextGeneration = 0,
        [Description("文本嵌入")] TextEmbedding = 1
    }
}
