using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess.Entities;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices.Routers
{
    public class LlmCompletionRouter : HttpClientHandler
    {
        private readonly LlmModel _llmModel;
        private readonly IOptions<LlmConfig> _llmConfigOptions;
        public LlmCompletionRouter(LlmModel llmModel, IOptions<LlmConfig> llmConfigOptions)
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
                switch (llmServiceProvider)
                {
                    case LlmServiceProvider.LLama:
                    case LlmServiceProvider.Ollama:
                    case LlmServiceProvider.HuggingFace:
                        var requestUrl = string.Format(_llmConfigOptions.Value?.ChatEndpoint, llmServiceProvider.ToString());
                        request.RequestUri = new Uri(requestUrl);
                        break;
                    case LlmServiceProvider.Zhipu:
                        request.RequestUri = new Uri("https://open.bigmodel.cn/api/paas/v4/chat/completions");
                        break;
                    case LlmServiceProvider.DeepSeek:
                        request.RequestUri = new Uri("https://api.deepseek.com/chat/completions");
                        break;
                    case LlmServiceProvider.OpenRouter:
                        request.RequestUri = new Uri("https://openrouter.ai/api/v1/chat/completions");
                        break;
                    case LlmServiceProvider.SiliconFlow:
                        request.RequestUri = new Uri("https://api.siliconflow.cn/v1/chat/completions");
                        break;
                    case LlmServiceProvider.MiniMax:
                        request.RequestUri = new Uri("https://api.minimax.chat/v1/text/chatcompletion_v2");
                        break;
                    case LlmServiceProvider.LingYi:
                        request.RequestUri = new Uri("https://api.lingyiwanwu.com/v1/chat/completions");
                        break;
                }

            }

            // 自定义地址
            if (!string.IsNullOrEmpty(_llmModel.BaseUrl))
                request.RequestUri = new Uri(_llmModel.BaseUrl);

            // 认证信息
            if (!string.IsNullOrEmpty(_llmModel.ApiKey))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _llmModel.ApiKey);

            request.Headers.Add(Constants.HttpRequestHeader_Provider, llmServiceProvider.ToString());
            return await base.SendAsync(request, cancellationToken);
        }

        private async Task<HttpRequestMessage> ProcessRequestMessages(HttpRequestMessage request)
        {
            var requestBody = await request.Content.ReadAsStringAsync();
            var dynamicObject = JObject.Parse(requestBody);
            var messages = dynamicObject["messages"].Value<JArray>();
            if (messages != null && messages.Children().Count() > 0)
            {
                var messageList = new List<object>();
                foreach (var message in messages.Children<JToken>())
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

                var payload = new
                {
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
