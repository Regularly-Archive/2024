using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess.Entities;

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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

            request.Headers.Add(Constants.HttpRequestHeader_Provider, llmServiceProvider.ToString());
            return base.SendAsync(request, cancellationToken);
        }
    }
}
