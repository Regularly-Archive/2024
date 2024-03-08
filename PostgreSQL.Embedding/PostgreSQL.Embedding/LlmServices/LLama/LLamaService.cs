using LLama.Abstractions;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices.LLama
{
    public class LLamaService : ILlmService
    {
        private readonly LLamaChatService _lLamaChatService;
        private readonly LLamaEmbeddingService _lLamaEmbeddingService;

        public LLamaService(LLamaChatService lLamaChatService, LLamaEmbeddingService lLamaEmbeddingService)
        {
            _lLamaChatService = lLamaChatService;
            _lLamaEmbeddingService = lLamaEmbeddingService;
        }

        public async Task ChatStream(OpenAIModel model, HttpContext HttpContext)
        {
            HttpContext.Response.Headers.Add("Content-Type", "text/event-stream");
            OpenAIStreamResult result = new OpenAIStreamResult();
            result.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            result.choices = new List<StreamChoicesModel>() { new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } } };
            string questions = model.messages.LastOrDefault().content;

            await foreach (var r in _lLamaChatService.ChatStreamAsync(questions))
            {
                result.choices[0].delta.content = r == null ? string.Empty : Convert.ToString(r);
                string message = $"data: {JsonConvert.SerializeObject(result)}\n\n";
                await HttpContext.Response.WriteAsync(message, Encoding.UTF8);
                await HttpContext.Response.Body.FlushAsync();
            }
            await HttpContext.Response.WriteAsync("data: [DONE]");
            await HttpContext.Response.Body.FlushAsync();
            await HttpContext.Response.CompleteAsync();
        }

        public async Task Chat(OpenAIModel model, HttpContext HttpContext)
        {
            string questions = model.messages.LastOrDefault().content;
            OpenAIResult result = new OpenAIResult();
            result.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            result.choices = new List<ChoicesModel>() { new ChoicesModel() { message = new OpenAIMessage() { role = "assistant" } } };

            result.choices[0].message.content = await _lLamaChatService.ChatAsync(questions); ;
            HttpContext.Response.ContentType = "application/json";
            await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(result));
            await HttpContext.Response.CompleteAsync();
        }

        public async Task Embedding(OpenAIEmbeddingModel model, HttpContext HttpContext)
        {
            var result = new OpenAIEmbeddingResult();
            result.data[0].embedding = await _lLamaEmbeddingService.Embedding(model.input[0]);
            HttpContext.Response.ContentType = "application/json";
            await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(result));
            await HttpContext.Response.CompleteAsync();
        }
    }
}
