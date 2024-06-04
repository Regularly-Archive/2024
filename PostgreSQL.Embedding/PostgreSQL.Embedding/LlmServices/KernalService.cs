using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.Utils;
using System.Reflection;

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
            var llmModel = await _llmModelRepository.SingleOrDefaultAsync(
                x => x.ModelType == (int)ModelType.TextGeneration && x.ModelName == app.TextModel
            );

            return (await GetKernel(llmModel));
        }

        public Task<Kernel> GetKernel(LlmModel llmModel)
        {
            var options = _serviceProvider.GetRequiredService<IOptions<LlmConfig>>();

            var httpClient = new HttpClient(new OpenAIChatHandler(llmModel, options));
            var kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(modelId: llmModel.ModelName, apiKey: llmModel.ApiKey ?? "sk-1234567890", httpClient: httpClient)
                .Build();

            kernel.Plugins.AddFromType<ConversationSummaryPlugin>();
            kernel.Plugins.AddFromType<TimePlugin>();

            kernel = kernel.ImportLlmPlugins(_serviceProvider);

            return Task.FromResult(kernel);
        }
    }
}
