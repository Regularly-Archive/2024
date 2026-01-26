using Microsoft.KernelMemory;
using PostgreSQL.Embedding.Domain.Entities;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface IMemoryService
    {
        Task<MemoryServerless> CreateByApp(LlmApp app);
        Task<MemoryServerless> CreateByKnowledgeBase(KnowledgeBase knowledgeBase);
    }
}
