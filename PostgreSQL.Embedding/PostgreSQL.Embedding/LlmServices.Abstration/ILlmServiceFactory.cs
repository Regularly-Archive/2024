using PostgreSQL.Embedding.Common;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface ILlmServiceFactory
    {
        ILlmService Create(LlmServiceProvider llmServiceProvider);
    }
}
