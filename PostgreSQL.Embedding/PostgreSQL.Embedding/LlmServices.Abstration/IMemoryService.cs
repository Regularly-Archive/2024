using Microsoft.KernelMemory;
using Microsoft.SemanticKernel.Memory;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IMemoryService
    {
        Task<MemoryServerless> CreateByApp(LlmApp app);
        Task<MemoryServerless> CreateByKnowledgeBase(KnowledgeBase knowledgeBase);
    }
}
