using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LlmServices.LLama;

namespace PostgreSQL.Embedding.LlmServices.HuggingFace
{
    public class HuggingFaceService : ILlmService
    {
        private readonly HuggingFaceEmbeddingService _huggingFaceEmbeddingService;
        public HuggingFaceService(HuggingFaceEmbeddingService embeddingService)
        {
            _huggingFaceEmbeddingService = embeddingService;
        }

        public Task Chat(OpenAIModel model, HttpContext HttpContext)
        {
            throw new NotImplementedException();
        }

        public Task ChatStream(OpenAIModel model, HttpContext HttpContext)
        {
            throw new NotImplementedException();
        }

        public async Task Embedding(OpenAIEmbeddingModel model, HttpContext HttpContext)
        {
            var result = new OpenAIEmbeddingResult();
            result.data[0].embedding = await _huggingFaceEmbeddingService.Embedding(model.input[0]);
            HttpContext.Response.ContentType = "application/json";
            await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(result));
            await HttpContext.Response.CompleteAsync();
        }
    }
}
