using Microsoft.SemanticKernel;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IKernelService
    {
        Kernel GetKernel(long appId);
    }
}
