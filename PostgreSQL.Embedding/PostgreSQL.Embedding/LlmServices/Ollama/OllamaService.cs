using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices.Ollama
{
    public class OllamaService : ILlmService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OllamaService> _logger;
        private readonly Stopwatch _stopwatch;
        private readonly string _baseUrl;
        public OllamaService(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<OllamaService> logger)
        {
            _baseUrl = configuration["OllamaConfig:BaseUrl"];
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _stopwatch = Stopwatch.StartNew();
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
            _stopwatch.Restart();
            _logger.LogInformation($"Start invoke {nameof(OllamaService)}::ChatStreamAsync()...");
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                httpClient.Timeout = TimeSpan.FromMinutes(5);

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri($"{_baseUrl}/v1/chat/completions"));
                httpRequest.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");

                var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
                httpResponse.EnsureSuccessStatusCode();

                using (var responseStream = await httpResponse.Content.ReadAsStreamAsync())
                {
                    using (var reader = new StreamReader(responseStream))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            if (string.IsNullOrEmpty(line)) continue;

                            if (!line.StartsWith("data: ")) continue;

                            var data = line.Substring("data: ".Length);
                            if (data.IndexOf("DONE") == -1)
                                yield return JObject.Parse(data)["choices"][0]["delta"]["content"].Value<string>();
                        }

                        _logger.LogInformation($"End invoke {nameof(OllamaService)}::ChatStreamAsync() in {_stopwatch.Elapsed.TotalSeconds} seconds.");
                        _stopwatch.Stop();
                    }
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
