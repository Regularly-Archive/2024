using Azure;
using Azure.AI.OpenAI;
using Azure.Core.Pipeline;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LlmServices.Routers;
using PostgreSQL.Embedding.Utils;
using System.Diagnostics;

namespace PostgreSQL.Embedding.LlmServices
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

        public async Task<Kernel> GetKernel(LlmApp app)
        {
            var llmModel = await _llmModelRepository.FindAsync(
                x => x.ModelType == (int)ModelType.TextGeneration && x.ModelName == app.TextModel
            );

            return (await GetKernel(llmModel, app.Id));
        }

        public async Task<Kernel> GetKernel(LlmModel llmModel, long? appId)
        {
            var options = _serviceProvider.GetRequiredService<IOptions<LlmConfig>>();

            var httpClient = new HttpClient(new LlmCompletionRouter(llmModel, options));
            httpClient.Timeout = Timeout.InfiniteTimeSpan;

            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddLogging(loggingBuilder => loggingBuilder.AddConsole().SetMinimumLevel(LogLevel.Information));

            var kernel = kernelBuilder
                .AddOpenAIChatCompletion(modelId: llmModel.ModelName, apiKey: llmModel.ApiKey ?? Guid.NewGuid().ToString(), httpClient: httpClient)
                .Build();

            kernel.Plugins.AddFromType<ConversationSummaryPlugin>();
            kernel.Plugins.AddFromType<TimePlugin>();
            kernel.Plugins.AddFromType<MathPlugin>();
            kernel = kernel.ImportLlmPlugins(_serviceProvider, appId);
            await kernel.AddMCPServer(
                name: "playwright",
                command: "npx",
                version: "1.0.0",
                args: ["-y", "@executeautomation/playwright-mcp-server"],
                env: null
            );

            await kernel.AddMCPServer(
                name: "memory",
                command: "npx",
                version: "1.0.0",
                args: ["-y", "@modelcontextprotocol/server-memory"],
                env: null
            );
            await kernel.AddMCPServer(
                name: "filesystem",
                command: "npx",
                version: "1.0.0",
                args: ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\Administrator"],
                env: null
            );
            await kernel.AddMCPServer(
                name: "git",
                command: "uvx",
                args: ["mcp-server-git", "--repository", "D:\\Projects\\2024"],
                env: null
            );
            return kernel;
        }
    }
}
