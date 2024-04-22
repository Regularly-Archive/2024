using Microsoft.KernelMemory.Handlers;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.KernelMemory;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices
{
    public class KnowledgeBaseTaskQueueService : IKnowledgeBaseTaskQueueService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IRepository<DocumentImportRecord> _importRecordRepository;
        private readonly ILogger<KnowledgeBaseTaskQueueService> _logger;
        public KnowledgeBaseTaskQueueService(IServiceProvider serviceProvider, IRepository<DocumentImportRecord> importRecordRepository, ILogger<KnowledgeBaseTaskQueueService> logger)
        {
            _serviceProvider = serviceProvider;
            _importRecordRepository = importRecordRepository;
            _logger = logger;
        }

        public async Task FetchAsync(int batchLimit = 5)
        {
            var records =
                (await _importRecordRepository.FindAsync(x => x.QueueStatus == (int)QueueStatus.Uploaded))
                .OrderBy(x => x.CreatedAt)
                .Take(batchLimit)
                .ToList();

            _logger.LogInformation($"There are {records.Count} files to be processed.");

            var tasks = records.Select(async record =>
            {
                using var serviceScope = _serviceProvider.CreateScope();
                var knowledgeBaseRespository = serviceScope.ServiceProvider.GetService<IRepository<KnowledgeBase>>();
                var knowledgeBase = await knowledgeBaseRespository.GetAsync(record.KnowledgeBaseId);
                if (knowledgeBase != null)
                {
                    if (record.DocumentType == (int)DocumentType.Text)
                    {
                        await HandleTextImportAsync(record, knowledgeBase);
                    }
                    else if (record.DocumentType == (int)DocumentType.Url)
                    {
                        await HandleUrlImportAsync(record, knowledgeBase);
                    }
                    else if (record.DocumentType == (int)DocumentType.File)
                    {
                        await HandleFileImportAsync(record, knowledgeBase);
                    }
                }
            });

            await Task.WhenAll(tasks);
        }

        private async Task HandleFileImportAsync(DocumentImportRecord record, KnowledgeBase knowledgeBase)
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var memoryService = serviceProviderScope.ServiceProvider.GetService<IMemoryService>();
            var memoryServerless = await memoryService.CreateByKnowledgeBase(knowledgeBase);
            var importRecordRepository = serviceProviderScope.ServiceProvider.GetService<IRepository<DocumentImportRecord>>();
            AddDefaultHandlers(memoryServerless, serviceProviderScope.ServiceProvider);

            var tags = new TagCollection
            {
                { KernelMemoryTags.TaskId, record.TaskId },
                { KernelMemoryTags.FileName, record.FileName },
                { KernelMemoryTags.KnowledgeBaseId, record.KnowledgeBaseId.ToString() },
            };
            var document = new Document(tags: tags, filePaths: new List<string> { record.Content });

            // 更新文件导入记录
            record.QueueStatus = (int)QueueStatus.Processing;
            record.ProcessStartTime = DateTime.Now;
            await importRecordRepository.UpdateAsync(record);

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

        private async Task HandleTextImportAsync(DocumentImportRecord record, KnowledgeBase knowledgeBase)
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var memoryService = serviceProviderScope.ServiceProvider.GetService<IMemoryService>();
            var memoryServerless = await memoryService.CreateByKnowledgeBase(knowledgeBase);
            var importRecordRepository = serviceProviderScope.ServiceProvider.GetService<IRepository<DocumentImportRecord>>();
            AddDefaultHandlers(memoryServerless, serviceProviderScope.ServiceProvider);

            var tags = new TagCollection
            {
                { KernelMemoryTags.TaskId, record.TaskId },
                { KernelMemoryTags.FileName, record.FileName},
                { KernelMemoryTags.KnowledgeBaseId, record.KnowledgeBaseId.ToString() },
            };

            // 更新文件导入记录
            record.QueueStatus = (int)QueueStatus.Processing;
            record.ProcessStartTime = DateTime.Now;
            await importRecordRepository.UpdateAsync(record);

            await memoryServerless.ImportTextAsync(record.Content, tags: tags, steps: new List<string>()
            {
                "extract_text",
                "split_text_in_partitions",
                "generate_embeddings",
                "save_memory_records",
                UpdateQueueStatusHandler.GetCurrentStepName()
            });
        }

        private async Task HandleUrlImportAsync(DocumentImportRecord record, KnowledgeBase knowledgeBase)
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var memoryService = serviceProviderScope.ServiceProvider.GetService<IMemoryService>();
            var memoryServerless = await memoryService.CreateByKnowledgeBase(knowledgeBase);
            var importRecordRepository = serviceProviderScope.ServiceProvider.GetService<IRepository<DocumentImportRecord>>();
            AddDefaultHandlers(memoryServerless, serviceProviderScope.ServiceProvider);

            var tags = new TagCollection
            {
                { KernelMemoryTags.TaskId, record.TaskId },
                { KernelMemoryTags.FileName, record.FileName},
                { KernelMemoryTags.KnowledgeBaseId, record.KnowledgeBaseId.ToString() },
                { KernelMemoryTags.Url, record.Content },
            };

            // 更新文件导入记录
            record.QueueStatus = (int)QueueStatus.Processing;
            record.ProcessStartTime = DateTime.Now;
            await importRecordRepository.UpdateAsync(record);

            await memoryServerless.ImportWebPageAsync(record.Content, tags: tags, steps: new List<string>()
            {
                "extract_text",
                "split_text_in_partitions",
                "generate_embeddings",
                "save_memory_records",
                UpdateQueueStatusHandler.GetCurrentStepName()
            });
        }

        private void AddDefaultHandlers(MemoryServerless memoryServerless, IServiceProvider serviceProvider)
        {
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<UpdateQueueStatusHandler>();

            memoryServerless.Orchestrator.AddHandler<TextExtractionHandler>("extract_text");
            memoryServerless.Orchestrator.AddHandler<TextPartitioningHandler>("split_text_in_partitions");
            memoryServerless.Orchestrator.AddHandler<GenerateEmbeddingsHandler>("generate_embeddings");
            memoryServerless.Orchestrator.AddHandler<SaveRecordsHandler>("save_memory_records");
            memoryServerless.Orchestrator.AddHandler(new UpdateQueueStatusHandler(serviceProvider, logger));
        }

        internal class UpdateQueueStatusHandler : IPipelineStepHandler
        {
            public string StepName => "update_quque_status";
            public static string GetCurrentStepName() => "update_quque_status";

            private readonly IRepository<DocumentImportRecord> _importRecordRepository;
            private readonly ILogger<UpdateQueueStatusHandler> _logger;
            public UpdateQueueStatusHandler(IServiceProvider serviceProvider, ILogger<UpdateQueueStatusHandler> logger)
            {
                _importRecordRepository = serviceProvider.GetService<IRepository<DocumentImportRecord>>();
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
                    var totalSeconds = Math.Round((record.ProcessEndTime.Value - record.ProcessStartTime.Value).TotalSeconds, 2);
                    record.ProcessDuartionTime = totalSeconds;
                    await _importRecordRepository?.UpdateAsync(record);
                    _logger.LogInformation($"Importing document '{fileName}' finished in {totalSeconds} seconds.");
                }

                return (true, pipeline);
            }
        }
    }
}
