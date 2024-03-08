using LLama.Common;
using LLama;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices.LLama
{
    public class LLamaEmbeddingService : ILlmEmbeddingService
    {
        private LLamaEmbedder _embedder;

        public LLamaEmbeddingService(IWebHostEnvironment environment, IConfiguration configuration)
        {
            var modelPath = Path.Combine(
                environment.ContentRootPath,
                configuration["LLamaConfig:ModelPath"]!
            );
            var @params = new ModelParams(modelPath) { EmbeddingMode = true };
            using var weights = LLamaWeights.LoadFromFile(@params);
            _embedder = new LLamaEmbedder(weights, @params);
        }


        public async Task<List<float>> Embedding(string text)
        {
            float[] embeddings = await _embedder.GetEmbeddings(text);
            return embeddings.ToList();
        }

        public void Dispose()
        {
            _embedder?.Dispose();
        }
    }
}
