using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Domain.Entities;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface IKernelService
    {
        Task<Kernel> GetKernel(LlmApp app, bool initializeTools = true);
        Task<Kernel> GetKernel(LlmModel model, long? appId = null, bool initializeTools = true);
    }
}
