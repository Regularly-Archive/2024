using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers;
using Microsoft.KernelMemory.Pipeline;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices
{
    public class KnowledgeBaseBackgroundService : BackgroundService
    {
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<KnowledgeBaseBackgroundService> _logger;
        public KnowledgeBaseBackgroundService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            _logger = loggerFactory.CreateLogger<KnowledgeBaseBackgroundService>();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_interval);
            _logger.LogInformation($"{nameof(KnowledgeBaseBackgroundService)} starts with interval {_interval.TotalSeconds} seconds.");

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    _logger.LogInformation($"{nameof(KnowledgeBaseTaskQueueService)} has been triggered at {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}.");
                    using var scope = _serviceProvider.CreateScope();
                    var knowledgeBaseTaskQueueService = scope.ServiceProvider.GetService<IKnowledgeBaseTaskQueueService>();
                    await knowledgeBaseTaskQueueService.FetchAsync();
                }
            }
            catch(Exception ex)
            {
                using var scope = _serviceProvider.CreateScope();
                var _documentImportRecordRepository = scope.ServiceProvider.GetService<IRepository<DocumentImportRecord>>();
                var records = await _documentImportRecordRepository.FindAsync(x => x.QueueStatus == (int)QueueStatus.Processing);
                foreach (var record in records)
                {
                    record.QueueStatus = (int)QueueStatus.Uploaded;
                    await _documentImportRecordRepository.UpdateAsync(record);
                }
                _logger.LogError($"KnowledgeQueueService exited.");
            }
        }
    }
}
