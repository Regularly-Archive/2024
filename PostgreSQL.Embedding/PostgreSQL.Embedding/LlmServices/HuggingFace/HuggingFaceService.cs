using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LlmServices.LLama;
using System.Net.Http;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices.HuggingFace
{
    public class HuggingFaceService : ILlmService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _baseUrl;
        public HuggingFaceService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _baseUrl = configuration["HuggingFaceConfig:BaseUrl"];
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
                var httpContent = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                var httpResponse = await httpClient.PostAsync(new Uri($"{_baseUrl}/v1/completions"), httpContent);
                httpResponse.EnsureSuccessStatusCode();

                var returnContent = await httpResponse.Content.ReadAsStringAsync();
                var completionResult = JsonConvert.DeserializeObject<OpenAICompatibleCompletionResult>(returnContent);

                return completionResult.Choices[0].Text;
            }
        }

        public async Task<List<float>> Embedding(OpenAIEmbeddingModel embeddingModel)
        {
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                var httpContent = new StringContent(JsonConvert.SerializeObject(embeddingModel), Encoding.UTF8, "application/json");
                var httpResponse = await httpClient.PostAsync(new Uri($"{_baseUrl}/v1/embeddings"), httpContent);
                httpResponse.EnsureSuccessStatusCode();

                var returnContent = await httpResponse.Content.ReadAsStringAsync();
                var dynamic = JObject.Parse(returnContent);

                return JsonConvert.DeserializeObject<List<float>>(dynamic["data"][0]["embedding"].ToString());
            }
        }
    }
}
