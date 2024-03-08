
using Microsoft.SemanticKernel.Connectors.HuggingFace;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices.HuggingFace
{
    public class HuggingFaceEmbeddingService : ILlmEmbeddingService
    {

        public HuggingFaceEmbeddingService(IConfiguration configuration)
        {
            
        }

        public Task<List<float>> Embedding(string text)
        {
            throw new NotImplementedException();
        }
    }
}
