using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LlmServices.HuggingFace;
using PostgreSQL.Embedding.LlmServices.LLama;

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
            }

            throw new Exception();
        }
    }
}
