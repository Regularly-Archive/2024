using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum LlmServiceProvider
    {
        [Description("OpenAI")] OpenAI = 0,
        [Description("LLama")] LLama = 1,
        [Description("HuggingFace")] HuggingFace = 2
    }
}
