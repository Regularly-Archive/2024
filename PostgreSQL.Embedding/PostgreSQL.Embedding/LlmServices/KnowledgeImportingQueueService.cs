using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices
{
    public class KnowledgeImportingQueueService : BackgroundService
    {
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(3);
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<KnowledgeImportingQueueService> _logger;
        public KnowledgeImportingQueueService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            _logger = loggerFactory.CreateLogger<KnowledgeImportingQueueService>();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_interval);
            _logger.LogInformation($"KnowledgeQueueService starts with interval {_interval.TotalSeconds} seconds.");

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    _logger.LogInformation($"KnowledgeQueueService has been triggered at {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}.");
                    using var scope = _serviceProvider.CreateScope();
                    var _knowledgeBaseService = scope.ServiceProvider.GetService<IKnowledgeBaseService>();
                    await _knowledgeBaseService.HandleImportingQueueAsync();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogError($"KnowledgeQueueService exited.");
            }
        }
    }
}
