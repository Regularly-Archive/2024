using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IKernelService
    {
        Task<Kernel> GetKernel(LlmApp app);
        Task<Kernel> GetKernel(LlmModel model);
    }
}
