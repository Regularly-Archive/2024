using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum LlmServiceProvider
    {
        [Description("OpenAI")] OpenAI = 0,
        [Description("LLama")] LLama = 1,
        [Description("HuggingFace")] HuggingFace = 2,
        [Description("Ollama")] Ollama = 3,
        [Description("智谱")] Zhipu = 4,
        [Description("深度求索")] DeepSeek = 5,
        [Description("OpenRouter")] OpenRouter = 6,
        [Description("硅基流动")] SiliconFlow = 7,
        [Description("MiniMax")] MiniMax = 8,
        [Description("零一万物")] LingYi = 9,
    }
}
