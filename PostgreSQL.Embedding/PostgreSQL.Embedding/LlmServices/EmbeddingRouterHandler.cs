using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices
{
    public class EmbeddingRouterHandler : HttpClientHandler
    {
        private readonly LlmModel _llmModel;
        private readonly IOptions<LlmConfig> _llmConfigOptions;
        public EmbeddingRouterHandler(LlmModel llmModel, IOptions<LlmConfig> llmConfigOptions)
        {
            _llmModel = llmModel;
            _llmConfigOptions = llmConfigOptions;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var llmServiceProvider = (LlmServiceProvider)_llmModel.ServiceProvider;
            if (llmServiceProvider != LlmServiceProvider.OpenAI)
            {
                // 接口重定向
                var requestUrl = string.Format(_llmConfigOptions.Value?.EmbeddingEndpoint, llmServiceProvider.ToString());
                request.RequestUri = new Uri(requestUrl);
            }

            if (!string.IsNullOrEmpty(_llmModel.BaseUrl))
            {
                request.RequestUri = new Uri(_llmModel.BaseUrl);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
