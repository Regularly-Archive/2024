using Azure.Search.Documents.Indexes.Models;
using HtmlAgilityPack;
using Irony.Ast;
using Microsoft.AspNetCore.Hosting;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers;
using Microsoft.KernelMemory.Pipeline;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
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
    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly IMemoryService _memoryService;
        private readonly IRepository<DocumentImportRecord> _importRecordRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly ILogger<KnowledgeBaseService> _logger;

        public KnowledgeBaseService(
            IServiceProvider serviceProvider, 
            IConfiguration configuration, 
            IMemoryService memoryService,
            IRepository<DocumentImportRecord> importRecordRepository,
            IRepository<KnowledgeBase> knowledgeBaseRepository,
            ILogger<KnowledgeBaseService> logger
            )
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _memoryService = memoryService;
            _importRecordRepository = importRecordRepository;
            _knowledgeBaseRepository = knowledgeBaseRepository;
            _logger = logger;
        }

        public Task<KnowledgeBase> CreateKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            return _knowledgeBaseRepository.AddAsync(knowledgeBase);
        }

        public Task UpdateKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            return _knowledgeBaseRepository.UpdateAsync(knowledgeBase);
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

        /// <summary>
        /// 从文档构建知识库
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="knowledgeBaseId">知识库ID</param>
        /// <param name="files">文件列表</param>
        /// <returns></returns>
        public async Task ImportKnowledgeFromFiles(string taskId, long knowledgeBaseId, IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);

                // 如果文件重复则直接忽略
                // Todo：考虑增加针对文件的 SHA 校验
                var record = await GetDocumentImportRecord(knowledgeBaseId, taskId, fileName);
                if (record != null) continue;

                // 增加文件导入记录
                _ = await AddDocumentImportRecordAsync(knowledgeBaseId, taskId, fileName, (int)QueueStatus.Uploaded);
            }
        }

        /// <summary>
        /// 异步处理队列任务
        /// </summary>
        /// <returns></returns>
        public async Task HandleImportingQueueAsync()
        {
            var webHostEnvironment = _serviceProvider.GetRequiredService<IWebHostEnvironment>();
            
            var records = await _importRecordRepository.FindAsync(x => x.QueueStatus == (int)QueueStatus.Uploaded);
            _logger.LogInformation($"There are {records.Count} files to be processed.");

            var tasks = records.Select(async record =>
            {
                var knowledgeBase = await GetKnowledgeBaseById(record.KnowledgeBaseId);
                var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);
                AddDefaultHandlers(memoryServerless);

                var embeddingTaskFolder = Path.Combine(webHostEnvironment.ContentRootPath, "Upload", record.TaskId);
                var embeddingFilePath = Path.Combine(embeddingTaskFolder, record.FileName);

                var tags = new TagCollection
                {
                    { KernelMemoryTags.TaskId, record.TaskId },
                    { KernelMemoryTags.FileName, record.FileName },
                    { KernelMemoryTags.KnowledgeBaseId, record.KnowledgeBaseId.ToString() },
                };
                var document = new Document(id: record.FileName, tags: tags, filePaths: new List<string> { embeddingFilePath });

                // 更新文件导入记录
                record.QueueStatus = (int)QueueStatus.Processing;
                record.ProcessStartTime = DateTime.Now;
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
            });

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 从网址构建知识库
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="knowledgeBaseId">知识库ID</param>
        /// <param name="url">网址</param>
        /// <returns></returns>
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

            var records = await _importRecordRepository.FindAsync(x => x.KnowledgeBaseId == knowledgeBaseId);
            foreach (var record in records)
            {
                await memoryServerless.DeleteDocumentAsync(record.FileName);
                await _importRecordRepository.DeleteAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.FileName == record.FileName);
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
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
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
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
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

        public async Task<List<KnowledgeBaseFile>> GetKnowledgeBaseFiles(long knowledgeBaseId)
        {
            var records = await _importRecordRepository.FindAsync(x => x.KnowledgeBaseId == knowledgeBaseId);
            return records.Select(x => new KnowledgeBaseFile()
            {
                FileName = x.FileName,
                KnowledgeBaseId = x.KnowledgeBaseId,
                QueueStatus = x.QueueStatus,
            })
            .ToList();
        }

        private async Task<KnowledgeBase> GetKnowledgeBaseById(long knowledgeBaseId)
        {
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            return knowledgeBase;
        }

        private void AddDefaultHandlers(MemoryServerless memoryServerless)
        {
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<UpdateQueueStatusHandler>();

            memoryServerless.Orchestrator.AddHandler<TextExtractionHandler>("extract_text");
            memoryServerless.Orchestrator.AddHandler<TextPartitioningHandler>("split_text_in_partitions");
            memoryServerless.Orchestrator.AddHandler<GenerateEmbeddingsHandler>("generate_embeddings");
            memoryServerless.Orchestrator.AddHandler<SaveRecordsHandler>("save_memory_records");
            memoryServerless.Orchestrator.AddHandler(new UpdateQueueStatusHandler(_importRecordRepository, logger));
        }

        private Task<DocumentImportRecord> GetDocumentImportRecord(long knowledgeBaseId, string taskId, string fileName)
        {
            return _importRecordRepository.SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.FileName == fileName);
        }

        private Task<DocumentImportRecord> AddDocumentImportRecordAsync(long knowledgeBaseId, string taskId, string fileName, QueueStatus queueStatus)
        {
            return _importRecordRepository.AddAsync(new DocumentImportRecord()
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

        private readonly IRepository<DocumentImportRecord> _importRecordRepository;
        private readonly ILogger<UpdateQueueStatusHandler> _logger;
        public UpdateQueueStatusHandler(IRepository<DocumentImportRecord> importRecordRepository, ILogger<UpdateQueueStatusHandler> logger)
        {
            _importRecordRepository = importRecordRepository;
            _logger = logger;
        }

        public async Task<(bool success, DataPipeline updatedPipeline)> InvokeAsync(DataPipeline pipeline, CancellationToken cancellationToken = default)
        {
            var taskId = pipeline.Tags[KernelMemoryTags.TaskId].FirstOrDefault();
            var fileName = pipeline.Tags[KernelMemoryTags.FileName].FirstOrDefault();
            var knowledgeBaseId = long.Parse(pipeline.Tags[KernelMemoryTags.KnowledgeBaseId].FirstOrDefault());


            var record = await _importRecordRepository.SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.TaskId == taskId && x.FileName == fileName);
            if (record != null)
            {
                record.QueueStatus = (int)QueueStatus.Complete;
                record.ProcessEndTime = DateTime.Now;
                var totalSeconds = Math.Round((record.ProcessEndTime - record.ProcessStartTime).TotalSeconds, 2);
                record.ProcessDuartionTime = totalSeconds;
                await _importRecordRepository?.UpdateAsync(record);
                _logger.LogInformation($"Importing document '{fileName}' finished in {totalSeconds} seconds.");
            }

            return (true, pipeline);
        }
    }
}
