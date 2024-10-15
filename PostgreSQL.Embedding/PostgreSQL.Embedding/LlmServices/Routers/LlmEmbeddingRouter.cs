using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Routers
{
    public class LlmEmbeddingRouter : HttpClientHandler
    {
        private readonly LlmModel _llmModel;
        private readonly IOptions<LlmConfig> _llmConfigOptions;
        public LlmEmbeddingRouter(LlmModel llmModel, IOptions<LlmConfig> llmConfigOptions)
        {
            _llmModel = llmModel;
            _llmConfigOptions = llmConfigOptions;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
                        request.RequestUri = new Uri("https://open.bigmodel.cn/api/paas/v4/embeddings");
                        break;
                    case LlmServiceProvider.SiliconFlow:
                        request.RequestUri = new Uri("ttps://api.siliconflow.cn/v1/embeddings");
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
            return base.SendAsync(request, cancellationToken);
        }
    }
}
