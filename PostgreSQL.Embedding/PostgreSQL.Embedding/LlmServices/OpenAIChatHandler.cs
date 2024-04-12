using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess.Entities;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices
{
    public class OpenAIChatHandler : HttpClientHandler
    {
        private readonly LlmModel _llmModel;
        private readonly IOptions<LlmConfig> _llmConfigOptions;
        public OpenAIChatHandler(LlmModel llmModel, IOptions<LlmConfig> llmConfigOptions)
        {
            _llmModel = llmModel;
            _llmConfigOptions = llmConfigOptions;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = await request.Content.ReadAsStringAsync();
            var dynamic = JObject.Parse(payload);
            var userMessage = dynamic["messages"].LastOrDefault();
            if (userMessage != null)
            {
                var content = userMessage["content"];
                if (content.Type == JTokenType.Array) 
                {
                    userMessage["content"] = content.FirstOrDefault().Value<string>("text");
                    request.Content = new StringContent(JsonConvert.SerializeObject(dynamic), Encoding.UTF8, "application/json");
                }
            }


            var llmServiceProvider = (LlmServiceProvider)_llmModel.ServiceProvider;
            if (llmServiceProvider != LlmServiceProvider.OpenAI)
            {
                var requestUrl = string.Format(_llmConfigOptions.Value?.ChatEndpoint, llmServiceProvider.ToString());
                request.RequestUri = new Uri(requestUrl);
            }

            if (!string.IsNullOrEmpty(_llmModel.BaseUrl))
            {
                request.RequestUri = new Uri(_llmModel.BaseUrl);
            }

            if (!string.IsNullOrEmpty(_llmModel.ApiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _llmModel.ApiKey);
            }

            request.Headers.Add(Constants.HttpRequestHeader_Provider, llmServiceProvider.ToString());
            return (await base.SendAsync(request, cancellationToken));
        }
    }
}
