
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices.HuggingFace
{
    public class HuggingFaceEmbeddingService : ILlmEmbeddingService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _baseUrl;
        public HuggingFaceEmbeddingService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _baseUrl = configuration["HuggingFaceConfig:BaseUrl"];
            _httpClientFactory = httpClientFactory;
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
