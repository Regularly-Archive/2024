using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LlmServices.HuggingFace;
using PostgreSQL.Embedding.LlmServices.LLama;
using PostgreSQL.Embedding.LlmServices.Ollama;

namespace PostgreSQL.Embedding.LlmServices
{
    public class LlmServiceFactory : ILlmServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;
        public LlmServiceFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ILlmService Create(LlmServiceProvider llmProvider = LlmServiceProvider.OpenAI)
        {
            switch (llmProvider)
            {
                case LlmServiceProvider.OpenAI:
                    return null;
                case LlmServiceProvider.LLama:
                    return _serviceProvider.GetService<LLamaService>();
                case LlmServiceProvider.HuggingFace:
                    return _serviceProvider.GetService<HuggingFaceService>();
                case LlmServiceProvider.Ollama:
                    return _serviceProvider.GetService<OllamaService>();
            }

            throw new Exception();
        }
    }
}
