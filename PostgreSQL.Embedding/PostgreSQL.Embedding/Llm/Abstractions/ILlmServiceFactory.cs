using PostgreSQL.Embedding.Common;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface ILlmServiceFactory
    {
        ILlmService Create(LlmServiceProvider llmServiceProvider);
    }
}
