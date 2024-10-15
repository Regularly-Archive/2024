using Microsoft.KernelMemory.AI;
using Microsoft.SemanticKernel.Embeddings;

namespace PostgreSQL.Embedding.LlmServices
{
    public class TextEmbeddingBatchGenerator : ITextEmbeddingBatchGenerator
    {
        #pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        public readonly ITextEmbeddingGenerationService _embeddingGenerationService;
        #pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        #pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        public TextEmbeddingBatchGenerator(ITextEmbeddingGenerationService embeddingGenerationService)
        #pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        {
            _embeddingGenerationService = embeddingGenerationService;
        }

        public async Task<Microsoft.KernelMemory.Embedding[]> GenerateEmbeddingBatchAsync(IEnumerable<string> textList, CancellationToken cancellationToken = default)
        {
            var embeddings = await _embeddingGenerationService.GenerateEmbeddingsAsync(textList.ToList());
            return embeddings.Select(x => new Microsoft.KernelMemory.Embedding(x)).ToArray();
        }
    }
}
