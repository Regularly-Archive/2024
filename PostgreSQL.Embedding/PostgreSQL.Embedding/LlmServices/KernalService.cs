﻿using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;

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
            var options = _serviceProvider.GetRequiredService<IOptions<LlmConfig>>();

            var llmModel = await _llmModelRepository.SingleOrDefaultAsync(
                x => x.ModelType == (int)ModelType.TextGeneration && x.ModelName == app.TextModel
            );

            var httpClient = new HttpClient(new OpenAIChatHandler(llmModel, options));
            var kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(modelId: app.TextModel, apiKey: llmModel.ApiKey ?? string.Empty, httpClient: httpClient)
                .Build();

            return kernel;
        }
    }
}
