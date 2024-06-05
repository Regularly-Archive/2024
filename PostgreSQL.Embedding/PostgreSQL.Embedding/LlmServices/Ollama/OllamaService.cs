using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices.Ollama
{
    public class OllamaService : ILlmService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _baseUrl;
        public OllamaService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _baseUrl = configuration["OllamaConfig:BaseUrl"];
            _httpClientFactory = httpClientFactory;
        }

        public async Task<string> ChatAsync(OpenAIModel request)
        {
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                var httpContent = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                var httpResponse = await httpClient.PostAsync(new Uri($"{_baseUrl}/v1/chat/completions"), httpContent);
                httpResponse.EnsureSuccessStatusCode();

                var returnContent = await httpResponse.Content.ReadAsStringAsync();
                var completionResult = JsonConvert.DeserializeObject<OpenAICompatibleResult>(returnContent);

                return completionResult.Choices[0].message.content;
            }
        }

        public async IAsyncEnumerable<string> ChatStreamAsync(OpenAIModel request)
        {
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                request.stream = false;
                var httpContent = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                var httpResponse = await httpClient.PostAsync(new Uri($"{_baseUrl}/v1/chat/completions"), httpContent);
                httpResponse.EnsureSuccessStatusCode();

                var returnContent = await httpResponse.Content.ReadAsStringAsync();
                var completionResult = JsonConvert.DeserializeObject<OpenAICompatibleResult>(returnContent);

                foreach (var item in completionResult.Choices[0].message.content)
                {
                    yield return item.ToString();
                }
            }
        }

        public async Task<string> CompletionAsync(OpenAICompletionModel request)
        {
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                var payload = new { model = request.model, prompt = request.prompt, stream = false };
                var httpContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var httpResponse = await httpClient.PostAsync(new Uri($"{_baseUrl}/api/generate"), httpContent);
                httpResponse.EnsureSuccessStatusCode();

                var returnContent = await httpResponse.Content.ReadAsStringAsync();
                var completionResult = JObject.Parse(returnContent);

                return completionResult["response"].Value<string>();
            }
        }

        public async Task<List<float>> Embedding(OpenAIEmbeddingModel embeddingModel)
        {
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                var payload = new { model = embeddingModel.model, prompt = embeddingModel.input[0] };
                var httpContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var httpResponse = await httpClient.PostAsync(new Uri($"{_baseUrl}/api/embeddings"), httpContent);
                httpResponse.EnsureSuccessStatusCode();

                var returnContent = await httpResponse.Content.ReadAsStringAsync();
                var completionResult = JObject.Parse(returnContent);

                return completionResult["embedding"].Value<List<float>>();
            }
        }
    }
}
