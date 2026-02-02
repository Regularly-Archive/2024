using System.Linq;
using PostgreSQL.Embedding.Common.Streaming;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Abstractions;

namespace PostgreSQL.Embedding.Llm.Core
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
            ILogger<ConversationService> logger)
        {
            _kernelService = kernelService;
            _memoryService = memoryService;
            _serviceProvider = serviceProvider;
            _llmAppRepository = llmAppRepository;
            _chatHistoryService = chatHistoryService;
            _logger = logger;
        }

        public async Task InvokeAsync(ConversationRequestModel model, long appId, HttpContext HttpContext, CancellationToken cancellationToken = default)
        {
            try
            {
                var app = await _llmAppRepository.GetAsync(appId);
                var kernel = await _kernelService.GetKernel(app);

                var input = model.Messages[model.Messages.Count - 1].content;

                var genericConversationService = new GenericConversationService(kernel, app, _serviceProvider, _chatHistoryService, HttpContext);
                await genericConversationService.InvokeAsync(model, input, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("The conversation is canceled.");
            }
        }

        public IAsyncEnumerable<ISseEvent> InvokeStreamingV2Async(
            ConversationRequestModel model,
            long appId,
            string input,
            string? conversationId = null,
            CancellationToken cancellationToken = default)
        {
            // Get app and kernel synchronously for streaming response
            var app = _llmAppRepository.GetAsync(appId).GetAwaiter().GetResult();
            if (app == null)
            {
                return Array.Empty<ISseEvent>().ToAsyncEnumerable();
            }

            var kernel = _kernelService.GetKernel(app).GetAwaiter().GetResult();

            // Create AgenticConversationService directly (not via DI)
            var agenticConversationService = new AgenticConversationService(
                kernel,
                app,
                _serviceProvider,
                _chatHistoryService);

            return agenticConversationService.InvokeAsync(model, input, conversationId, cancellationToken);
        }
    }
}
