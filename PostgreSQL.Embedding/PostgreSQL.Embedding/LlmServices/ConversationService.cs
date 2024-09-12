using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices
{
    public class ConversationService : IConversationService
    {
        private readonly IMemoryService _memoryService;
        private readonly IKernelService _kernelService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IRepository<LlmApp> _llmAppRepository;
        private readonly IChatHistoriesService _chatHistoryService;
        private readonly ILogger<ConversationService> _logger;
        public ConversationService(
            IServiceProvider serviceProvider,
            IRepository<LlmApp> llmAppRepository,
            IKernelService kernelService,
            IMemoryService memoryService,
            IChatHistoriesService chatHistoryService,
            ILogger<ConversationService> logger
            )
        {
            _kernelService = kernelService;
            _memoryService = memoryService;
            _serviceProvider = serviceProvider;
            _llmAppRepository = llmAppRepository;
            _chatHistoryService = chatHistoryService;
            _logger = logger;
        }

        public async Task InvokeAsync(OpenAIModel model, long appId, HttpContext HttpContext, CancellationToken cancellationToken = default)
        {
            try
            {
                var app = await _llmAppRepository.GetAsync(appId);
                var kernel = await _kernelService.GetKernel(app);

                var input = model.messages[model.messages.Count - 1].content;
                switch (app.AppType)
                {
                    case (int)LlmAppType.Chat:
                        var genericConversationService = new GenericConversationService(kernel, app, _serviceProvider, _chatHistoryService);
                        await genericConversationService.InvokeAsync(model, HttpContext, input, cancellationToken);
                        break;
                    case (int)LlmAppType.Knowledge:
                        var ragConversationService = new RAGConversationService(kernel, app, _serviceProvider, _memoryService, _chatHistoryService);
                        await ragConversationService.InvokeAsync(model, HttpContext, input, cancellationToken);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("The conversation is canceled.");
            }
        }
    }
}
