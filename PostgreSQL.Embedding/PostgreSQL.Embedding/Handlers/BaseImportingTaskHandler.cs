using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers;
using Microsoft.KernelMemory.Pipeline;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.Services;

namespace PostgreSQL.Embedding.Handlers
{
    public class BaseImportingTaskHandler : IImportingTaskHandler
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly List<string> _steps = new List<string>()
        {
            "extract_text",
            "split_text_in_partitions",
            "generate_embeddings",
            "save_memory_records",
            UpdateQueueStatusHandler.GetCurrentStepName()
        };

        public BaseImportingTaskHandler(IServiceProvider serviceProvider) 
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// 异步导入逻辑
        /// </summary>
        /// <param name="record"></param>
        /// <param name="knowledgeBase"></param>
        /// <returns></returns>
        public async Task Handle(DocumentImportRecord record, KnowledgeBase knowledgeBase)
        {
            // 为 KernleMemory 绑定默认的 Handler
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var memoryService = serviceProviderScope.ServiceProvider.GetService<IMemoryService>();
            var memoryServerless = await memoryService.CreateByKnowledgeBase(knowledgeBase);
            var importRecordRepository = serviceProviderScope.ServiceProvider.GetService<IRepository<DocumentImportRecord>>();
            AddDefaultHandlers(memoryServerless, serviceProviderScope.ServiceProvider);

            // 将当前任务状态设置为 Processing 
            await SetTaskAsProcessing(record);

            // 构建 & 导入文档
            var document = await BuildDocument(record);
            await memoryServerless.ImportDocumentAsync(document, steps: _steps);
        }

        /// <summary>
        /// 构建文档
        /// </summary>
        /// <param name="record">当前导入记录</param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public virtual Task<Document> BuildDocument(DocumentImportRecord record)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 判断当前 Handler 是否匹配与任务匹配
        /// </summary>
        /// <param name="record"></param>
        /// <returns></returns>
        public virtual bool IsMatch(DocumentImportRecord record) => false;

        /// <summary>
        /// 设置当前任务状态为 Processing
        /// </summary>
        /// <param name="record"></param>
        /// <returns></returns>
        private async Task SetTaskAsProcessing(DocumentImportRecord record)
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var importRecordRepository = serviceProviderScope.ServiceProvider.GetService<IRepository<DocumentImportRecord>>();
            var notificationService = serviceProviderScope.ServiceProvider.GetService<INotificationService>();
            var messageRepository = serviceProviderScope.ServiceProvider.GetService<IRepository<SystemMessage>>();

            // 更新文件导入记录
            record.QueueStatus = (int)QueueStatus.Processing;
            record.ProcessStartTime = DateTime.Now;
            await importRecordRepository.UpdateAsync(record);

            // 发送消息
            var documentParsingStartedEvent = record.CreateDocumentParsingStartedEvent();
            await notificationService.SendTo(record.CreatedBy, documentParsingStartedEvent);
            await messageRepository.AddAsync(new SystemMessage()
            {
                Title = "系统消息",
                Content = documentParsingStartedEvent.Content,
                Type = "System",
                IsRead = false
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
            private readonly IRepository<SystemMessage> _messageRepository;
            private readonly INotificationService _notificationService;
            private readonly ILogger<UpdateQueueStatusHandler> _logger;
            public UpdateQueueStatusHandler(IServiceProvider serviceProvider, ILogger<UpdateQueueStatusHandler> logger)
            {
                _importRecordRepository = serviceProvider.GetService<IRepository<DocumentImportRecord>>();
                _notificationService = serviceProvider.GetRequiredService<INotificationService>();
                _messageRepository = serviceProvider.GetService<IRepository<SystemMessage>>();
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

                    // 发送消息
                    var documentReadyEvent = record.CreateDocumentReadyEvent();
                    await _notificationService.SendTo(record.CreatedBy, documentReadyEvent);
                    await _messageRepository.AddAsync(new SystemMessage()
                    {
                        Title = "系统消息",
                        Type = "System",
                        Content = documentReadyEvent.Content,
                        IsRead = false
                    });
                }

                return (true, pipeline);
            }
        }
    }
}
