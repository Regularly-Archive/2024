using Microsoft.KernelMemory.Handlers;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.KernelMemory;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.Services;
using PostgreSQL.Embedding.Common.Models.Notification;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Utils;
using PostgreSQL.Embedding.Handlers;
using System.Reflection.Metadata;

namespace PostgreSQL.Embedding.LlmServices
{
    public class KnowledgeBaseTaskQueueService : IKnowledgeBaseTaskQueueService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IRepository<DocumentImportRecord> _importRecordRepository;
        private readonly ILogger<KnowledgeBaseTaskQueueService> _logger;
        private readonly INotificationService _notificationService;
        private readonly IRepository<SystemMessage> _messageRepository;
        public KnowledgeBaseTaskQueueService(
            IServiceProvider serviceProvider,
            IRepository<DocumentImportRecord> importRecordRepository,
            ILogger<KnowledgeBaseTaskQueueService> logger,
            INotificationService notificationService,
            IRepository<SystemMessage> messageRepository
            )
        {
            _serviceProvider = serviceProvider;
            _importRecordRepository = importRecordRepository;
            _notificationService = notificationService;
            _messageRepository = messageRepository;
            _logger = logger;
        }

        public async Task FetchAsync(int batchLimit = 5)
        {
            var records =
                (await _importRecordRepository.FindAsync(x => x.QueueStatus == (int)QueueStatus.Uploaded))
                .OrderBy(x => x.CreatedAt)
                .Take(batchLimit)
                .ToList();

            if (!records.Any()) return;

            _logger.LogInformation($"There are {records.Count} documents to be processed.");

            var tasks = records.Select(async record =>
            {
                using var serviceScope = _serviceProvider.CreateScope();
                var knowledgeBaseRespository = serviceScope.ServiceProvider.GetService<IRepository<KnowledgeBase>>();
                var knowledgeBase = await knowledgeBaseRespository.GetAsync(record.KnowledgeBaseId);
                var handlers = serviceScope.ServiceProvider.GetServices<IImportingTaskHandler>();
                var handler = handlers.FirstOrDefault(x => x.IsMatch(record));
                if (knowledgeBase != null && handler != null)
                {
                    _logger.LogInformation($"The task '{record.TaskId}/{record.FileName}' starts to execute...");
                    await handler.Handle(record, knowledgeBase);
                    _logger.LogInformation($"The task '{record.TaskId}/{record.FileName}' executes finsished.");
                }
            });

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                var errorMsg = ex is AggregateException ?
                    string.Join(",", (ex as AggregateException).InnerExceptions.Select(x => x.Message)) : ex.Message;

                await RollbackAsync();
                _logger.LogWarning($"Exception '{errorMsg}' occurs when executing document importing tasks, operations will be rollback.");
            }
        }

        private async Task RollbackAsync()
        {
            var records = await _importRecordRepository.FindAsync(x => x.QueueStatus == (int)QueueStatus.Processing);
            foreach (var record in records)
            {
                record.QueueStatus = (int)QueueStatus.Uploaded;
                await _importRecordRepository.UpdateAsync(record);
            }
        }
    }
}
