using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    /// <summary>
    /// 负责统一大模型调用
    /// </summary>
    public interface ILlmService
    {
        Task Chat(OpenAIModel model, HttpContext HttpContext);
        Task ChatStream(OpenAIModel model, HttpContext HttpContext);
        Task Embedding(OpenAIEmbeddingModel model, HttpContext HttpContext);
    }
}
