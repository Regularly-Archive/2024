
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices.HuggingFace
{
    public class HuggingFaceEmbeddingService : ILlmEmbeddingService
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        public HuggingFaceEmbeddingService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<List<float>> Embedding(string text)
        {
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                var payload = new
                {
                    model = _configuration["HuggingFaceConfig:EmbedingModel"],
                    input = new List<string> { text }
                };
                var httpContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var httpResponse = await httpClient.PostAsync(new Uri("http://127.0.0.1:8003/v1/embeddings"), httpContent);
                httpResponse.EnsureSuccessStatusCode();

                var returnContent = await httpResponse.Content.ReadAsStringAsync();
                var dynamic = JObject.Parse(returnContent);

                return JsonConvert.DeserializeObject<List<float>>(dynamic["data"][0]["embedding"].ToString());
            }
        }
    }
}
