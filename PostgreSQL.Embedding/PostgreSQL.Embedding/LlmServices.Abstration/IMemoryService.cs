using Microsoft.KernelMemory;
using Microsoft.SemanticKernel.Memory;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IMemoryService
    {
        Task<T> CreateByApp<T>(LlmApp app) where T : class, IKernelMemory;
        Task<MemoryServerless> CreateByKnowledgeBase(KnowledgeBase knowledgeBase);
    }
}
