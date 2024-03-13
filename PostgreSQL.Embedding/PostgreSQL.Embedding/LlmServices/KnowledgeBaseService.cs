using DocumentFormat.OpenXml.Math;
using JetBrains.Annotations;
using LLama;
using LLamaSharp.KernelMemory;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.Postgres;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Dtos;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.Handlers;
using PostgreSQL.Embedding.LlmServices.Abstration;
using SqlSugar;
using System.Security.Policy;

namespace PostgreSQL.Embedding.LlmServices
{
    public class KnowledgeBaseService : CrudBaseService<KnowledgeBase, KnowledgeBase, KnowledgeBase>, IKnowledgeBaseService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly IMemoryService _memoryService;

        public KnowledgeBaseService(IServiceProvider serviceProvider, IConfiguration configuration, IMemoryService memoryService)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _memoryService = memoryService;
        }

        public Task<KnowledgeBase> CreateKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            return AddAsync(knowledgeBase);
        }

        public Task UpdateKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            return UpdateAsync(knowledgeBase);
        }

        public async Task<List<KnowledgeDetail>> GetKnowledgeBaseDetails(long knowledgeBaseId)
        {
            // 查询知识库
            var knowledgeBase = await GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            // Todo:
            var memoryFilter = new MemoryFilter();
            memoryFilter.ByTag(KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString());


            return new List<KnowledgeDetail>();
        }

        public async Task ImportKnowledgeFromFiles(string taskId, long knowledgeBaseId, IEnumerable<string> files)
        {
            // 查询知识库
            var knowledgeBase = await GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var tags = new TagCollection
                {
                    { KernelMemoryTags.TaskId, taskId },
                    { KernelMemoryTags.FileName, fileName },
                    { KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString() },
                };
                var document = new Document(id: fileName, tags: tags, filePaths: new List<string> { file });
                await memoryServerless.ImportDocumentAsync(document);
            }
        }

        public async Task ImportKnowledgeFromUrl(string taskId, long knowledgeBaseId, string url)
        {
            // 查询知识库
            var knowledgeBase = await GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var tags = new TagCollection
            {
                { KernelMemoryTags.TaskId, taskId },
                { KernelMemoryTags.FileName, url },
                { KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString() },
            };
            await memoryServerless.ImportWebPageAsync(url, tags: tags);
        }

        public async Task DeleteKnowledgeById(long knowledgeBaseId)
        {
            // 查询知识库
            var knowledgeBase = await GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            // Todo
            await Task.CompletedTask;
        }

        public async Task DeleteKnowledgeByFileName(long knowledgeBaseId, string fileName)
        {
            // 查询知识库
            var knowledgeBase = await GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            // Todo
            await Task.CompletedTask;
        }

        public async Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double minRelevance = 0, int limit = 5)
        {
            var kmSearchResult = new KMSearchResult() { Question = question };

            // 查询知识库
            var knowledgeBase = await GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var memoryFilter = new MemoryFilter()
                .ByTag(KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString());

            var searchResult = await memoryServerless.SearchAsync(question, filter: memoryFilter, minRelevance:minRelevance, limit:limit);
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

        public async Task<KMAskResult> AskAsync(long knowledgeBaseId, string question, double minRelevance = 0)
        {
            var askResult = new KMAskResult() { Question = question };
            // 查询知识库
            var knowledgeBase = await GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var memoryFilter = new MemoryFilter()
                .ByTag(KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString());

            var memoryAnswer = await memoryServerless.AskAsync(question, filter: memoryFilter, minRelevance: 0);
            askResult.Answer = memoryAnswer.Result;

            if (memoryAnswer.RelevantSources.Any())
            {
                askResult.RelevantSources = memoryAnswer.RelevantSources.Select(x => new KMCitation()
                {
                    SourceName = x.SourceName,
                    Partitions = x.Partitions.Select(y => new KMPartition(y)).ToList()
                })
                .ToList();
            }
            return askResult;
        }

        public async Task<bool> IsDocumentReady(long knowledgeBaseId, string fileName)
        {
            // 查询知识库
            var knowledgeBase = await GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);
            return (await memoryServerless.IsDocumentReadyAsync(fileName));
        }


        public async Task<bool> IsTaskReady(long knowledgeBaseId, string taskId)
        {
            // 查询知识库
            var knowledgeBase = await GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            return false;
        }
    }
}
