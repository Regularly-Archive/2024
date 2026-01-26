namespace PostgreSQL.Embedding.Llm.Abstractions
{
    /// <summary>
    /// 负责统一大模型调用
    /// </summary>
    public interface ILlmService : ILlmChatService, ILlmCompletionService, ILlmEmbeddingService
    {

    }
}
