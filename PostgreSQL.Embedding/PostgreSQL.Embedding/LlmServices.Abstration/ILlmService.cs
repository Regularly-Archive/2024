using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    /// <summary>
    /// 负责统一大模型调用
    /// </summary>
    public interface ILlmService : ILlmChatService, ILlmCompletionService, ILlmEmbeddingService
    {

    }
}
