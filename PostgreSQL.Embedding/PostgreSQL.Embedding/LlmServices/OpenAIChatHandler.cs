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
            request = await ProcessRequestMessages(request);

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

        private async Task<HttpRequestMessage> ProcessRequestMessages(HttpRequestMessage request)
        {
            var requestBody = await request.Content.ReadAsStringAsync();
            var dynamicObject = JObject.Parse(requestBody);
            var messages = dynamicObject["messages"].Value<JArray>();
            if (messages != null && messages.Children().Count() > 0)
            {
                var messageList = new List<object>();
                foreach(var message in messages.Children<JToken>())
                {
                    var messageRole = message["role"].Value<string>();
                    var messageContent = message["content"];
                    if (messageContent.Children<JToken>().Count() == 0)
                    {
                        messageList.Add(new { role = messageRole, content = messageContent });
                    }
                    else
                    {
                        var innerContent = messageContent.First();
                        messageList.Add(new { role = messageRole, content = innerContent["text"].Value<string>() });
                    }
                }

                var payload = new { 
                    model = dynamicObject["model"].Value<string>(), 
                    messages = messageList,
                    stream = dynamicObject.ContainsKey("stream")
                };
                request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            }

            
            return request;
        }
    }
}
