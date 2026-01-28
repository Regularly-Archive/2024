using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Connectors.Anthropic;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Llm.Routers;
using PostgreSQL.Embedding.Utils;

namespace PostgreSQL.Embedding.Llm.Core
{
    public class KernalService : IKernelService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IRepository<LlmModel> _llmModelRepository;

        public KernalService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _llmModelRepository = _serviceProvider.GetService<IRepository<LlmModel>>();
        }

        public async Task<Kernel> GetKernel(LlmApp app, bool initializeTools = true)
        {
            var llmModel = await _llmModelRepository.FindAsync(
                x => x.ModelType == (int)ModelType.TextGeneration && x.ModelName == app.TextModel
            );

            return (await GetKernel(llmModel, app.Id, initializeTools));
        }

        public async Task<Kernel> GetKernel(LlmModel llmModel, long? appId, bool initializeTools = true)
        {
            var httpClient = new HttpClient(new LlmCompletionRouter(llmModel, _serviceProvider.GetRequiredService<IOptions<LlmConfig>>()))
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddLogging(loggingBuilder => loggingBuilder.AddConsole().SetMinimumLevel(LogLevel.Information));
            kernelBuilder.Services.AddScoped<AgentExecutionContext>();

            // 根据 LlmModel 自动选择 OpenAI 或 Anthropic
            kernelBuilder.AddChatCompletionFromModel(llmModel, httpClient);

            var kernel = kernelBuilder.Build();

            kernel.Plugins.AddFromType<ConversationSummaryPlugin>();
            kernel.Plugins.AddFromType<TimePlugin>();
            kernel.Plugins.AddFromType<MathPlugin>();

            if (initializeTools)
            {
                kernel = await kernel.ImportLlmPluginsAsync(_serviceProvider, appId);
                kernel = await kernel.ImportMCPServer(_serviceProvider, appId);
            }

            return kernel;
        }
    }
}
