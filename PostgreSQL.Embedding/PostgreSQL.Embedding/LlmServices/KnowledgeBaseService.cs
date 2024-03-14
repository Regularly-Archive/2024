using Azure.Search.Documents.Indexes.Models;
using HtmlAgilityPack;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers;
using Microsoft.KernelMemory.Pipeline;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using SqlSugar;
using System;
using System.Runtime.CompilerServices;
using Constants = PostgreSQL.Embedding.Common.Constants;

namespace PostgreSQL.Embedding.LlmServices
{
    public class KnowledgeBaseService : CrudBaseService<KnowledgeBase, KnowledgeBase, KnowledgeBase>, IKnowledgeBaseService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly IMemoryService _memoryService;
        private readonly SimpleClient<DocumentImportRecord> _importRecordRepository;

        public KnowledgeBaseService(IServiceProvider serviceProvider, IConfiguration configuration, IMemoryService memoryService)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _memoryService = memoryService;
            _importRecordRepository = _serviceProvider.GetService<SimpleClient<DocumentImportRecord>>();
        }

        public Task<KnowledgeBase> CreateKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            return AddAsync(knowledgeBase);
        }

        public Task UpdateKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            return UpdateAsync(knowledgeBase);
        }

        // Todo
        public async Task<List<KMPartition>> GetKnowledgeBaseDetails(long knowledgeBaseId, string fileName = null)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var filters = new List<MemoryFilter>
            {
                new MemoryFilter().ByTag(KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString())
            };

            if (!string.IsNullOrEmpty(fileName))
                filters.Add(new MemoryFilter().ByTag(KernelMemoryTags.FileName, fileName));

            var searchResult = await memoryServerless.SearchSummariesAsync(filters: filters);
            return searchResult.SelectMany(x => x.Partitions).Select(x => new KMPartition(x)).ToList();
        }

        public async Task ImportKnowledgeFromFiles(string taskId, long knowledgeBaseId, IEnumerable<string> files)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);
            AddDefaultHandlers(memoryServerless);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);

                // 如果文件重复则直接忽略
                // Todo：考虑增加针对文件的 SHA 校验
                var record = await GetDocumentImportRecord(knowledgeBaseId, taskId, fileName);
                if (record != null) continue;

                var tags = new TagCollection
                {
                    { KernelMemoryTags.TaskId, taskId },
                    { KernelMemoryTags.FileName, fileName },
                    { KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString() },
                };
                var document = new Document(id: fileName, tags: tags, filePaths: new List<string> { file });

                // 增加文件导入记录
                record = await AddDocumentImportRecordAsync(knowledgeBaseId, taskId, fileName, (int)QueueStatus.Uploaded);

                // 更新文件导入记录
                record.QueueStatus = (int)QueueStatus.Processing;
                await UpdateDocumentImportRecordAsync(record);

                // 导入文档
                await memoryServerless.ImportDocumentAsync(document, steps: new List<string>()
                {
                    "extract_text",
                    "split_text_in_partitions",
                    "generate_embeddings",
                    "save_memory_records",
                    UpdateQueueStatusHandler.GetCurrentStepName()
                });
            }
        }

        public async Task ImportKnowledgeFromUrl(string taskId, long knowledgeBaseId, string url)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);
            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var tags = new TagCollection
            {
                { KernelMemoryTags.TaskId, taskId },
                { KernelMemoryTags.FileName, url },
                { KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString() },
            };

            await memoryServerless.ImportWebPageAsync(url, tags: tags, steps: new List<string> { UpdateQueueStatusHandler.GetCurrentStepName() });
        }

        public async Task DeleteKnowledgesById(long knowledgeBaseId)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var records = await _importRecordRepository.GetListAsync(x => x.KnowledgeBaseId == knowledgeBaseId);
            foreach (var record in records)
            {
                await memoryServerless.DeleteDocumentAsync(record.FileName);
            }
        }

        public async Task DeleteKnowledgesByFileName(long knowledgeBaseId, string fileName)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);
            await memoryServerless.DeleteDocumentAsync(fileName);
            await _importRecordRepository.DeleteAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.FileName == fileName);
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
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);
            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);
            return (await memoryServerless.IsDocumentReadyAsync(fileName));
        }

        private async Task<KnowledgeBase> GetKnowledgeBaseById(long knowledgeBaseId)
        {
            var knowledgeBase = await GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            return knowledgeBase;
        }

        private void AddDefaultHandlers(MemoryServerless memoryServerless)
        {
            memoryServerless.Orchestrator.AddHandler<TextExtractionHandler>("extract_text");
            memoryServerless.Orchestrator.AddHandler<TextPartitioningHandler>("split_text_in_partitions");
            memoryServerless.Orchestrator.AddHandler<GenerateEmbeddingsHandler>("generate_embeddings");
            memoryServerless.Orchestrator.AddHandler<SaveRecordsHandler>("save_memory_records");
            memoryServerless.Orchestrator.AddHandler(new UpdateQueueStatusHandler(_importRecordRepository));
        }

        private Task<DocumentImportRecord> GetDocumentImportRecord(long knowledgeBaseId, string taskId, string fileName)
        {
            return _importRecordRepository.GetFirstAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.FileName == fileName);
        }

        private Task<DocumentImportRecord> AddDocumentImportRecordAsync(long knowledgeBaseId, string taskId, string fileName, QueueStatus queueStatus)
        {
            return _importRecordRepository.InsertReturnEntityAsync(new DocumentImportRecord()
            {
                TaskId = taskId,
                FileName = fileName,
                QueueStatus = (int)queueStatus,
                KnowledgeBaseId = knowledgeBaseId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = Constants.Admin,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = Constants.Admin,
            });
        }

        private async Task UpdateDocumentImportRecordAsync(DocumentImportRecord documentImportRecord)
        {
            await _importRecordRepository.UpdateAsync(documentImportRecord);
        }
    }

    internal class UpdateQueueStatusHandler : IPipelineStepHandler
    {
        public string StepName => "update_quque_status";
        public static string GetCurrentStepName() => "update_quque_status";

        private readonly SimpleClient<DocumentImportRecord> _importRecordRepository;
        public UpdateQueueStatusHandler(SimpleClient<DocumentImportRecord> importRecordRepository)
        {
            _importRecordRepository = importRecordRepository;
        }

        public async Task<(bool success, DataPipeline updatedPipeline)> InvokeAsync(DataPipeline pipeline, CancellationToken cancellationToken = default)
        {
            var taskId = pipeline.Tags[KernelMemoryTags.TaskId].FirstOrDefault();
            var fileName = pipeline.Tags[KernelMemoryTags.FileName].FirstOrDefault();
            var knowledgeBaseId = long.Parse(pipeline.Tags[KernelMemoryTags.KnowledgeBaseId].FirstOrDefault());


            var record = await _importRecordRepository.GetFirstAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.TaskId == taskId && x.FileName == fileName);
            if (record != null)
            {
                record.QueueStatus = (int)QueueStatus.Complete;
                await _importRecordRepository?.UpdateAsync(record);
            }

            return (true, pipeline);
        }
    }
}
