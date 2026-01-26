using Microsoft.KernelMemory;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.KernelMemory;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Abstractions;

namespace PostgreSQL.Embedding.Llm.Services.Retrieval
{
    public class VectorsRetrievalService : IKnowledgeRetrievalService
    {
        private RetrievalType _retrievalType;
        public RetrievalType RetrievalType
        {
            get { return _retrievalType; }
        }

        private readonly IMemoryService _memoryService;
        private readonly IRepository<KnowledgeBase> _repository;
        public VectorsRetrievalService(IMemoryService memoryService, IRepository<KnowledgeBase> repository)
        {
            _repository = repository;
            _memoryService = memoryService;
            _retrievalType = RetrievalType.Vectors;
        }

        public async Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double minRelevance = 0, int limit = 5)
        {
            var kmSearchResult = new KMSearchResult() { Question = question };

            // 查询知识库
            var knowledgeBase = await _repository.GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var memoryFilter = new MemoryFilter()
                .ByTag(KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString());

            var searchResult = await memoryServerless.SearchAsync(question, filter: memoryFilter, minRelevance: minRelevance, limit: limit);
            if (searchResult.NoResult) return kmSearchResult;

            if (searchResult.Results.Any())
            {
                kmSearchResult.RelevantSources =
                    searchResult.Results.Select(x => new KMCitation()
                    {
                        SourceName = x.SourceName,
                        Partitions = x.Partitions.Select(y => new KMPartition(y)).ToList()
                    })
                    .ToList();
            }
            return kmSearchResult;
        }
    }
}
