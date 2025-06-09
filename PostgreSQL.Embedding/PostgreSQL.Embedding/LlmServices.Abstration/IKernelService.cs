using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IKernelService
    {
        Task<Kernel> GetKernel(LlmApp app, bool initializeTools = true);
        Task<Kernel> GetKernel(LlmModel model, long? appId = null, bool initializeTools = true);
    }
}
